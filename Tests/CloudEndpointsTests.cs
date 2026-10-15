using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Core.Interfaces.ServerClient;
using Core.Interfaces.Storage;
using Core.Models;
using Core.Models.Credentials;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tests.Fakes;

namespace Tests;

public class CloudEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly FakeSyncServer _server = new();

    public CloudEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IProfileStorage>();
            services.AddSingleton<IProfileStorage, FakeProfileStorage>();
            services.RemoveAll<IProfileServerClient>();
            services.AddSingleton<IProfileServerClient>(_server);
            services.RemoveAll<IServerSessionService>();
            services.AddSingleton<IServerSessionService, Core.Implementations.ServerClient.ServerSessionService>();
        }));
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r)
        => await r.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task Login_WithServerUrl_RemembersAddressAndUser_WithoutLeakingToken()
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/cloud/login",
            new { username = "kirill", password = "secret", serverUrl = "https://sync.example.org:5227/" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var session = await Json(await client.GetAsync("/cloud/session"));
        Assert.True(session.GetProperty("loggedIn").GetBoolean());
        Assert.Equal("kirill", session.GetProperty("username").GetString());
        Assert.Equal("https://sync.example.org:5227", session.GetProperty("serverUrl").GetString());
        Assert.DoesNotContain("token", session.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_WithBadUrl_Returns400()
    {
        var client = _factory.CreateClient();
        var r = await client.PostAsJsonAsync("/cloud/login",
            new { username = "u", password = "p", serverUrl = "ftp://nope" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Login_SyncServerDown_Returns502NotInternalError()
    {
        _server.Down = true;
        var client = _factory.CreateClient();
        var r = await client.PostAsJsonAsync("/cloud/login",
            new { username = "u", password = "p", serverUrl = "http://127.0.0.1:1" });
        Assert.Equal(HttpStatusCode.BadGateway, r.StatusCode);
    }

    [Fact]
    public async Task Logout_ClearsSession()
    {
        var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/cloud/login",
            new { username = "u", password = "p", serverUrl = "http://127.0.0.1:9" });
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/cloud/logout", null)).StatusCode);
        var session = await Json(await client.GetAsync("/cloud/session"));
        Assert.False(session.GetProperty("loggedIn").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/cloud/download")).StatusCode);
    }

    [Fact]
    public async Task UploadThenDownload_RoundTripsProfiles_AndReportsCount()
    {
        var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/cloud/login",
            new { username = "u", password = "p", serverUrl = "http://127.0.0.1:9" });
        var profile = SavedProfile.Create("demo", new HostProfile(string.Empty, Protocol.Local, new AnonymousAuth()));
        (await client.PostAsJsonAsync("/profiles", profile)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/cloud/upload/{profile.Id}", null)).StatusCode);

        var download = await Json(await client.GetAsync("/cloud/download"));
        Assert.Equal(1, download.GetProperty("count").GetInt32());
    }

    private sealed class FakeSyncServer : IProfileServerClient
    {
        private readonly List<SavedProfile> _profiles = new();
        public bool Down { get; set; }

        private void Check()
        {
            if (Down) throw new HttpRequestException("down");
        }

        public Task<bool> RegisterAsync(string username, string password, CancellationToken ct = default)
        {
            Check();
            return Task.FromResult(true);
        }

        public Task<string?> LoginAsync(string username, string password, CancellationToken ct = default)
        {
            Check();
            return Task.FromResult<string?>("fake-token");
        }

        public Task<List<SavedProfile>> GetProfilesAsync(string token, CancellationToken ct = default)
        {
            Check();
            return Task.FromResult(_profiles.ToList());
        }

        public Task<SavedProfile?> UploadProfileAsync(string token, SavedProfile profile, CancellationToken ct = default)
        {
            Check();
            _profiles.Add(profile);
            return Task.FromResult<SavedProfile?>(profile);
        }

        public Task<bool> DeleteProfileAsync(string token, Guid profileId, CancellationToken ct = default)
        {
            Check();
            return Task.FromResult(true);
        }
    }
}
