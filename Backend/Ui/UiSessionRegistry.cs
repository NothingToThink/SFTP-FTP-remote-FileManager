using System.Collections.Concurrent;

namespace Backend.Ui;

public class UiSessionRegistry(ILogger<UiSessionRegistry> logger) : IUiSessionRegistry
{
    private readonly ConcurrentDictionary<string, UiSession> _sessions = new();

    public event Action<string>? Disconnected;

    public IReadOnlyCollection<UiSession> Sessions => _sessions.Values.ToArray();

    public bool IsConnected(string sessionId) => _sessions.ContainsKey(sessionId);

    public void Add(string connectionId) =>
        _sessions[connectionId] = new UiSession(connectionId, DateTimeOffset.UtcNow);

    public void Remove(string connectionId)
    {
        if (!_sessions.TryRemove(connectionId, out _))
            return;

        // A faulty subscriber must not break the others or the hub's disconnect handling.
        foreach (var handler in Disconnected?.GetInvocationList().Cast<Action<string>>() ?? [])
        {
            try
            {
                handler(connectionId);
            }
            catch (Exception e)
            {
                logger.LogError(e, "UI disconnect handler failed.");
            }
        }
    }
}
