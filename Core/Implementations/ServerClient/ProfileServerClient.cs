using System.Net.Http.Headers;
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

    public async Task<string?> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/auth/login", new ServerAuthRequest(username, password), ct);
        if (!response.IsSuccessStatusCode) return null;

        var result = await response.Content.ReadFromJsonAsync<ServerLoginResponse>(cancellationToken: ct);
        return result?.AccessToken;
    }

    public async Task<List<SavedProfile>> GetProfilesAsync(string token, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/profiles");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return new List<SavedProfile>();

        var profiles = await response.Content.ReadFromJsonAsync<List<SavedProfile>>(cancellationToken: ct);
        return profiles ?? new List<SavedProfile>();
    }

    public async Task<SavedProfile?> UploadProfileAsync(string token, SavedProfile profile, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/profiles")
        {
            Content = JsonContent.Create(profile)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;

        return await response.Content.ReadFromJsonAsync<SavedProfile>(cancellationToken: ct);
    }

    public async Task<bool> DeleteProfileAsync(string token, Guid profileId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/profiles?profileId={profileId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _httpClient.SendAsync(request, ct);
        return response.IsSuccessStatusCode;
    }
}