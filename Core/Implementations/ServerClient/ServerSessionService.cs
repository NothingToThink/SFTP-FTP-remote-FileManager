using Core.Interfaces.ServerClient;

namespace Core.Implementations.ServerClient;

public class ServerSessionService : IServerSessionService
{
    public string? AccessToken { get; set; }
    public string? ServerUrl { get; set; }
    public string? Username { get; set; }
}
