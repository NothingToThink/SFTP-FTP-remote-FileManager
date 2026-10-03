using System.Net.Http.Json;
using Core.Implementations.Manager;
using Core.Interfaces.Manager;
using Core.Models;
using Core.Models.Credentials;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tests.Fakes;

namespace Tests;

public class ConnectionsEndpointTests :
    IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ConnectionsEndpointTests(WebApplicationFactory<Program> factory)
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
    public async Task SavedProfile_To_ConnectionManager_Returns_Correct_Id()
    {
        var client = _factory.CreateClient();
        var profile = SavedProfile.Create("new profile",
            new HostProfile(string.Empty, Protocol.Local, new AnonymousAuth()));
        var postResponse = await client.PostAsJsonAsync("/connections",  profile);
        postResponse.EnsureSuccessStatusCode();
        var id = await postResponse.Content.ReadFromJsonAsync<Guid>();
        var getResponse = await client.GetAsync("/connections");
        var ids = await getResponse.Content.ReadFromJsonAsync<List<Guid>>();
        Assert.Contains(id, ids ?? Enumerable.Empty<Guid>());
    }
}