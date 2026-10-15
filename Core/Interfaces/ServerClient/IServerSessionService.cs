namespace Core.Interfaces.ServerClient;

public interface IServerSessionService
{
    string? AccessToken { get; set; }

    /// <summary>Address of the user's own sync server chosen in the UI; null means the configured default.</summary>
    string? ServerUrl { get; set; }

    /// <summary>Login the current cloud session was opened with (never the password).</summary>
    string? Username { get; set; }

    bool IsLoggedIn => !string.IsNullOrEmpty(AccessToken);
}
