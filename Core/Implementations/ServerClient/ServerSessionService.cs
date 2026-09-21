using Core.Interfaces.ServerClient;

namespace Core.Implementations.ServerClient;

public class ServerSessionService : IServerSessionService
{
    public Guid? CurrentUserId { get; set; }
}