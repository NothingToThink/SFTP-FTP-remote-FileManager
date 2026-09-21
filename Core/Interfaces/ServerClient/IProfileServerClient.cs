using System.Text.Json.Serialization;
using Core.Models.Credentials;

namespace Core.Interfaces.ServerClient;

public record ServerAuthRequest(string Username, string Password);
public record ServerLoginResponse([property: JsonPropertyName("userId")] Guid UserId);


public interface IProfileServerClient
{
    Task<bool> RegisterAsync(string username, string password, CancellationToken ct = default);
    Task<Guid?> LoginAsync(string username, string password, CancellationToken ct = default);
    Task<List<SavedProfile>> GetProfilesAsync(Guid userId, CancellationToken ct = default);
    Task<SavedProfile?> UploadProfileAsync(Guid userId, SavedProfile profile, CancellationToken ct = default);
}