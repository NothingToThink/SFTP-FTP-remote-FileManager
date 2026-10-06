using Core.Interfaces.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tests.Fakes;
using System.Net.Http.Json;
using Core.Models;
using Core.Models.Credentials;

namespace Tests;

public class ProfilesEndpointTests :
    IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ProfilesEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IProfileStorage>();
                services.AddSingleton<IProfileStorage, FakeProfileStorage>();
            });
        });
    }

    [Fact]
    public async Task GetProfilesIdList_Empty_ReturnsOk()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/profiles");
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task SaveProfile_ThenGetProfileIdList_ContainsSavedId()
    {
        var client = _factory.CreateClient();
        var profile = SavedProfile.Create(
            "test-profile",
            new HostProfile(string.Empty, Protocol.Local, new AnonymousAuth()));
        var postResponse = await client.PostAsJsonAsync("/profiles", profile);
        postResponse.EnsureSuccessStatusCode();
        var getResponse = await client.GetAsync("/profiles");
        var ids = await getResponse.Content.ReadFromJsonAsync<List<Guid>>();
        Assert.Contains(profile.Id, ids ?? Enumerable.Empty<Guid>());
    }
}