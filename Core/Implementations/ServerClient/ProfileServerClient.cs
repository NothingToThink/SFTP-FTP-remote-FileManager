using System.Net.Http.Json;
using Core.Models.Credentials;
using Core.Interfaces.ServerClient;

namespace Core.Implementations.ServerClient;

public class ProfileServerClient : IProfileServerClient
{
    private readonly HttpClient _httpClient;

    public ProfileServerClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<bool> RegisterAsync(string username, string password, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/auth/register", new ServerAuthRequest(username, password), ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<Guid?> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/auth/login", new ServerAuthRequest(username, password), ct);
        if (!response.IsSuccessStatusCode) return null;

        var result = await response.Content.ReadFromJsonAsync<ServerLoginResponse>(cancellationToken: ct);
        return result?.UserId;
    }

    public async Task<List<SavedProfile>> GetProfilesAsync(Guid userId, CancellationToken ct = default)
    {
        var profiles = await _httpClient.GetFromJsonAsync<List<SavedProfile>>($"/profiles?userId={userId}", ct);
        return profiles ?? new List<SavedProfile>();
    }

    public async Task<SavedProfile?> UploadProfileAsync(Guid userId, SavedProfile profile, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsJsonAsync($"/profiles?userId={userId}", profile, ct);
        if (!response.IsSuccessStatusCode) return null;

        return await response.Content.ReadFromJsonAsync<SavedProfile>(cancellationToken: ct);
    }
}