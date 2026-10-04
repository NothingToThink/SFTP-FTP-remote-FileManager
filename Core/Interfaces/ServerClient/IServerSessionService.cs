namespace Core.Interfaces.ServerClient;

public interface IServerSessionService
{
    string? AccessToken { get; set; }
    bool IsLoggedIn => !string.IsNullOrEmpty(AccessToken);
}