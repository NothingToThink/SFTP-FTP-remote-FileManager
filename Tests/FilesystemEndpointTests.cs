using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Core.Interfaces.Manager;
using Core.Models;
using Core.Models.Credentials;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tests.Fakes;

namespace Tests;

/// <summary>
/// Guards dir/size, dir/copy and dir/move: they silently disappeared from the controller
/// in #36 (BACKEND-BUGS.md, BUG-3) and nothing failed. Runs against LocalConnection in a temp folder.
/// </summary>
public class FilesystemEndpointTests :
    IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public FilesystemEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IConnectionManager>();
                services.TryAddSingleton<IConnectionManager, FakeConnectionManager>();
            });
        });
    }

    [Fact]
    public async Task GetDirSize_ReturnsTotalSizeOfNestedFiles()
    {
        var (client, id) = await CreateConnectionAsync();
        await UploadAsync(client, id, "src/a.txt", "12345");
        await UploadAsync(client, id, "src/nested/b.txt", "1234567");

        var response = await client.GetAsync($"/connections/{id}/filesystem/dir/size?path=src");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(12L, await response.Content.ReadFromJsonAsync<long>());
    }

    [Fact]
    public async Task CopyDir_CopiesNestedContentAndKeepsSource()
    {
        var (client, id) = await CreateConnectionAsync();
        await UploadAsync(client, id, "src/a.txt", "a");
        await UploadAsync(client, id, "src/nested/b.txt", "b");

        var response = await client.PostAsJsonAsync($"/connections/{id}/filesystem/dir/copy",
            new { sourcePath = "src", targetPath = "dst", canOverride = false });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(await FileExistsAsync(client, id, "dst/a.txt"));
        Assert.True(await FileExistsAsync(client, id, "dst/nested/b.txt"));
        Assert.True(await FileExistsAsync(client, id, "src/a.txt"));
    }

    [Fact]
    public async Task MoveDir_MovesDirectoryAndRemovesSource()
    {
        var (client, id) = await CreateConnectionAsync();
        await UploadAsync(client, id, "src/a.txt", "a");

        var response = await client.PatchAsJsonAsync($"/connections/{id}/filesystem/dir/move",
            new { sourcePath = "src", targetPath = "moved", canOverride = false });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(await FileExistsAsync(client, id, "moved/a.txt"));
        Assert.False(await DirExistsAsync(client, id, "src"));
    }

    // --- path в query (GET/DELETE не читают тело) ---

    public static TheoryData<string, string> PathEndpoints => new()
    {
        { "GET", "info" },
        { "GET", "dir/size" },
        { "GET", "file/exists" },
        { "GET", "dir/exists" },
        { "DELETE", "file" },
        { "DELETE", "dir" },
    };

    [Theory]
    [MemberData(nameof(PathEndpoints))]
    public async Task PathEndpoint_WithoutPath_Returns400WithError(string method, string endpoint)
    {
        var (client, id) = await CreateConnectionAsync();

        foreach (var query in new[] { "", "?path=" })
        {
            var response = await client.SendAsync(
                new HttpRequestMessage(new HttpMethod(method), $"/connections/{id}/filesystem/{endpoint}{query}"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Query parameter 'path' is required.", body.GetProperty("error").GetString());
        }
    }

    [Theory]
    [InlineData("GET", "info")]
    [InlineData("GET", "dir/size")]
    [InlineData("DELETE", "file")]
    [InlineData("DELETE", "dir")]
    public async Task PathEndpoint_NonExistentPath_IsNotSuccess(string method, string endpoint)
    {
        var (client, id) = await CreateConnectionAsync();

        var response = await client.SendAsync(
            new HttpRequestMessage(new HttpMethod(method), $"/connections/{id}/filesystem/{endpoint}?path=missing"));

        // Exact code arrives with the BUG-2 fix (ArgumentException is not mapped yet and gives 500).
        Assert.False(response.IsSuccessStatusCode, $"{method} {endpoint}: {response.StatusCode}");
    }

    [Fact]
    public async Task GetInfo_WithPathInQuery_ReturnsFileItem()
    {
        var (client, id) = await CreateConnectionAsync();
        await UploadAsync(client, id, "docs/a.txt", "12345");

        var info = await client.GetFromJsonAsync<JsonElement>(
            $"/connections/{id}/filesystem/info?path={Uri.EscapeDataString("docs/a.txt")}");

        Assert.Equal("a.txt", info.GetProperty("name").GetString());
        Assert.Equal(5, info.GetProperty("size").GetInt64());
        Assert.False(info.GetProperty("isDirectory").GetBoolean());
    }

    [Fact]
    public async Task ExistsEndpoints_WithPathInQuery_DistinguishFilesAndDirs()
    {
        var (client, id) = await CreateConnectionAsync();
        await UploadAsync(client, id, "docs/a.txt", "a");

        Assert.True(await FileExistsAsync(client, id, "docs/a.txt"));
        Assert.False(await FileExistsAsync(client, id, "docs"));
        Assert.True(await DirExistsAsync(client, id, "docs"));
        Assert.False(await DirExistsAsync(client, id, "docs/a.txt"));
    }

    [Fact]
    public async Task DeleteDir_WithPathInQuery_RemovesDirectory()
    {
        var (client, id) = await CreateConnectionAsync();
        await CreateDirAsync(client, id, "docs");

        var response = await client.DeleteAsync($"/connections/{id}/filesystem/dir?path=docs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(await DirExistsAsync(client, id, "docs"));
    }

    [Theory]
    [InlineData("a b.txt")]
    [InlineData("c++.txt")]
    [InlineData("#1.txt")]
    [InlineData("50%.txt")]
    [InlineData("a&b=c.txt")]
    [InlineData("файл.txt")]
    public async Task FileLifecycle_WithSpecialCharsInPath_RoundTrips(string name)
    {
        var (client, id) = await CreateConnectionAsync();
        var query = $"?path={Uri.EscapeDataString(name)}";

        var create = await client.PostAsJsonAsync($"/connections/{id}/filesystem/file", name);
        create.EnsureSuccessStatusCode();
        Assert.True(await FileExistsAsync(client, id, name));

        var info = await client.GetFromJsonAsync<JsonElement>($"/connections/{id}/filesystem/info{query}");
        Assert.Equal(name, info.GetProperty("name").GetString());

        var delete = await client.DeleteAsync($"/connections/{id}/filesystem/file{query}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        Assert.False(await FileExistsAsync(client, id, name));
    }

    private async Task<(HttpClient Client, Guid Id)> CreateConnectionAsync()
    {
        var client = _factory.CreateClient();
        var profile = SavedProfile.Create("filesystem-tests",
            new HostProfile(string.Empty, Protocol.Local, new AnonymousAuth()));

        var response = await client.PostAsJsonAsync("/connections", profile);
        response.EnsureSuccessStatusCode();
        return (client, await response.Content.ReadFromJsonAsync<Guid>());
    }

    private static async Task UploadAsync(HttpClient client, Guid id, string remotePath, string content)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(content)), "file", Path.GetFileName(remotePath));

        var response = await client.PostAsync(
            $"/connections/{id}/filesystem/file/upload?remotePath={Uri.EscapeDataString(remotePath)}", form);
        response.EnsureSuccessStatusCode();
    }

    private static async Task CreateDirAsync(HttpClient client, Guid id, string path)
    {
        var response = await client.PostAsJsonAsync($"/connections/{id}/filesystem/dir", path);
        response.EnsureSuccessStatusCode();
    }

    private static Task<bool> FileExistsAsync(HttpClient client, Guid id, string path)
        => GetBoolAsync(client, $"/connections/{id}/filesystem/file/exists", path);

    private static Task<bool> DirExistsAsync(HttpClient client, Guid id, string path)
        => GetBoolAsync(client, $"/connections/{id}/filesystem/dir/exists", path);

    private static async Task<bool> GetBoolAsync(HttpClient client, string url, string path)
    {
        var response = await client.GetAsync($"{url}?path={Uri.EscapeDataString(path)}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<bool>();
    }
}
