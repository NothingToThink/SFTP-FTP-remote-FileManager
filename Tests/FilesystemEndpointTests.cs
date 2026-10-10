using System.Net;
using System.Net.Http.Json;
using System.Text;
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
