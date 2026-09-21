namespace Core.Interfaces.ServerClient;

public interface IServerSessionService
{
    Guid? CurrentUserId { get; set; }
    bool IsLoggedIn => CurrentUserId.HasValue;
}