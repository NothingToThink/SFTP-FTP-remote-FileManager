using System.Net.Http.Headers;
using System.Net.Http.Json;
using Core.Models.Credentials;
using Core.Interfaces.ServerClient;

namespace Core.Implementations.ServerClient;

public class ProfileServerClient : IProfileServerClient
{
    private readonly HttpClient _httpClient;
    private readonly IServerSessionService _session;

    public ProfileServerClient(HttpClient httpClient, IServerSessionService session)
    {
        _httpClient = httpClient;
        _session = session;
    }

    // The sync server is the user's own: its address comes from the session, the configured
    // BaseAddress is only the default. Absolute request URIs make HttpClient ignore BaseAddress.
    private Uri Url(string path)
    {
        var baseUri = _session.ServerUrl is { Length: > 0 } custom
            ? new Uri(custom.TrimEnd('/') + "/")
            : _httpClient.BaseAddress
              ?? throw new InvalidOperationException("Sync server address is not set.");
        return new Uri(baseUri, path.TrimStart('/'));
    }

    // An expired JWT must not look like "no profiles on the server".
    private static void ThrowIfExpired(HttpResponseMessage response)
    {
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            throw new UnauthorizedAccessException("Sync server rejected the session token.");
    }

    public async Task<bool> RegisterAsync(string username, string password, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsJsonAsync(Url("/auth/register"), new ServerAuthRequest(username, password), ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<string?> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsJsonAsync(Url("/auth/login"), new ServerAuthRequest(username, password), ct);
        if (!response.IsSuccessStatusCode) return null;

        var result = await response.Content.ReadFromJsonAsync<ServerLoginResponse>(cancellationToken: ct);
        return result?.AccessToken;
    }

    public async Task<List<SavedProfile>> GetProfilesAsync(string token, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Url("/profiles"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _httpClient.SendAsync(request, ct);
        ThrowIfExpired(response);
        if (!response.IsSuccessStatusCode) return new List<SavedProfile>();

        var profiles = await response.Content.ReadFromJsonAsync<List<SavedProfile>>(cancellationToken: ct);
        return profiles ?? new List<SavedProfile>();
    }

    public async Task<SavedProfile?> UploadProfileAsync(string token, SavedProfile profile, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Url("/profiles"))
        {
            Content = JsonContent.Create(profile)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _httpClient.SendAsync(request, ct);
        ThrowIfExpired(response);
        if (!response.IsSuccessStatusCode) return null;

        return await response.Content.ReadFromJsonAsync<SavedProfile>(cancellationToken: ct);
    }

    public async Task<bool> DeleteProfileAsync(string token, Guid profileId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, Url($"/profiles?profileId={profileId}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _httpClient.SendAsync(request, ct);
        return response.IsSuccessStatusCode;
    }
}