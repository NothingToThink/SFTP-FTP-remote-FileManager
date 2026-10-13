using System.Text.Json.Serialization;
using Core.Models.Credentials;

namespace Core.Interfaces.ServerClient;

public record ServerAuthRequest(string Username, string Password);
public record ServerLoginResponse([property: JsonPropertyName("accessToken")] string AccessToken);

public interface IProfileServerClient
{
    Task<bool> RegisterAsync(string username, string password, CancellationToken ct = default);
    Task<string?> LoginAsync(string username, string password, CancellationToken ct = default);
    Task<List<SavedProfile>> GetProfilesAsync(string token, CancellationToken ct = default);
    Task<SavedProfile?> UploadProfileAsync(string token, SavedProfile profile, CancellationToken ct = default);
    Task<bool> DeleteProfileAsync(string token, Guid profileId, CancellationToken ct = default);
}