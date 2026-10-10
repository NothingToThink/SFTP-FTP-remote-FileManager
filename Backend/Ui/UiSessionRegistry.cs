using System.Collections.Concurrent;

namespace Backend.Ui;

public interface IUiSessionRegistry
{
    /// <summary>Raised after a client is removed; the argument is its ConnectionId.</summary>
    event Action<string>? ClientDisconnected;

    void Add(string connectionId);
    void Remove(string connectionId);
    bool IsConnected(string connectionId);
    IReadOnlyCollection<UiSession> GetSessions();
}

public class UiSessionRegistry(TimeProvider timeProvider) : IUiSessionRegistry
{
    private readonly ConcurrentDictionary<string, UiSession> _sessions = new();

    public event Action<string>? ClientDisconnected;

    public void Add(string connectionId) =>
        _sessions[connectionId] = new UiSession(connectionId, timeProvider.GetUtcNow());

    public void Remove(string connectionId)
    {
        if (!_sessions.TryRemove(connectionId, out _))
            return;

        // One faulty subscriber must not break the others or the hub's OnDisconnectedAsync.
        foreach (var handler in ClientDisconnected?.GetInvocationList() ?? [])
        {
            try
            {
                ((Action<string>)handler)(connectionId);
            }
            catch
            {
                // Subscribers (UiBridge) catch their own errors; nothing sensible to do here.
            }
        }
    }

    public bool IsConnected(string connectionId) => _sessions.ContainsKey(connectionId);

    public IReadOnlyCollection<UiSession> GetSessions() => _sessions.Values.ToList();
}
