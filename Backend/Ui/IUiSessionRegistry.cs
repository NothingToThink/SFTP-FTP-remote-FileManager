namespace Backend.Ui;

public record UiSession(string ConnectionId, DateTimeOffset ConnectedAt);

/// <summary>Connected UI clients. Filled by <c>UiHub</c> on connect and disconnect.</summary>
public interface IUiSessionRegistry
{
    IReadOnlyCollection<UiSession> Sessions { get; }

    bool IsConnected(string sessionId);

    /// <summary>Raised after a client is removed; the argument is its connection id.</summary>
    event Action<string> Disconnected;

    void Add(string connectionId);

    void Remove(string connectionId);
}
