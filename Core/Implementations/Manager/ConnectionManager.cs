using System.Collections.Concurrent;
using Core.Interfaces.Manager;
using Core.Interfaces.Protocol;
using Core.Interfaces.Factory;
using Core.Models.Credentials;
using Core.PortForwarding;
using Core.Ssh;

namespace Core.Implementations.Manager;

/// <summary>
/// Registry of live connections. Registered as a singleton and reached from concurrent requests,
/// so the backing store is concurrent and every removal path disposes what it removes.
/// </summary>
public class ConnectionManager(
    IConnectionFactory connectionFactory,
    IPortForwardingManager portForwardingManager) : IConnectionManager
{
    private readonly ConcurrentDictionary<Guid, ConnectionEntry> _connections = new();

    public Guid CreateConnection(SavedProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var connection = connectionFactory.CreateConnection(profile.HostProfile);
        _connections[connection.Id] = new ConnectionEntry(connection);
        return connection.Id;
    }

    public List<Guid> GetConnectionIdList() => _connections.Keys.ToList();

    public Connection GetConnection(Guid id)
    {
        if (!_connections.TryGetValue(id, out var entry))
            throw new KeyNotFoundException($"Connection {id} was not found.");

        entry.Touch();
        return entry.Connection;
    }

    public bool TryGetConnection(Guid id, out Connection? connection)
    {
        if (_connections.TryGetValue(id, out var entry))
        {
            entry.Touch();
            connection = entry.Connection;
            return true;
        }

        connection = null;
        return false;
    }

    public void DeleteConnection(Guid id) => DeleteConnectionAsync(id).GetAwaiter().GetResult();

    public async Task DeleteConnectionAsync(Guid id, CancellationToken ct = default)
    {
        if (!_connections.TryRemove(id, out var entry))
            return;

        await ReleaseAsync(entry, ct);
    }

    public async Task<int> SweepIdleAsync(TimeSpan idleTimeout, CancellationToken ct = default)
    {
        var deadline = DateTimeOffset.UtcNow - idleTimeout;
        var collected = 0;

        foreach (var (id, entry) in _connections.ToArray())
        {
            ct.ThrowIfCancellationRequested();

            if (!entry.IsIdleSince(deadline))
                continue;

            // TryRemove guards against a request grabbing the same connection concurrently: the
            // loser of the race simply finds nothing to remove.
            if (!_connections.TryRemove(id, out var removed))
                continue;

            await ReleaseAsync(removed, ct);
            collected++;
        }

        return collected;
    }

    private async Task ReleaseAsync(ConnectionEntry entry, CancellationToken ct)
    {
        // Tear the tunnels down first: they hold references to the SSH client we are about to dispose.
        await portForwardingManager.RemoveSessionAsync(entry.SessionId ?? entry.Connection.Id);

        try
        {
            await entry.Connection.DisconnectAsync(ct);
        }
        catch (Exception)
        {
            // Already-dead transports are expected here; disposal below is what must not be skipped.
        }

        await entry.Connection.DisposeAsync();
    }

    private sealed class ConnectionEntry(Connection connection)
    {
        private long _lastUsedTicks = DateTimeOffset.UtcNow.UtcTicks;

        public Connection Connection { get; } = connection;

        /// <summary>Session id for SSH-backed connections, used to find their forwarding rules.</summary>
        public Guid? SessionId { get; } = (connection as ISshSessionProvider)?.Session.Id;

        public void Touch() => Interlocked.Exchange(ref _lastUsedTicks, DateTimeOffset.UtcNow.UtcTicks);

        public bool IsIdleSince(DateTimeOffset deadline)
        {
            // A connection with live port forwards is in use even if nobody is browsing files
            // through it, so it must never be collected on idleness alone.
            if (Connection is ISshSessionProvider { Session: { } session })
            {
                if (session.ActiveForwardCount > 0)
                    return false;

                // The session tracks its own activity (file ops, forwarding, port scans), which is
                // finer-grained than what the manager sees.
                if (session.LastUsedAtUtc > deadline)
                    return false;
            }

            return new DateTimeOffset(Interlocked.Read(ref _lastUsedTicks), TimeSpan.Zero) <= deadline;
        }
    }
}
