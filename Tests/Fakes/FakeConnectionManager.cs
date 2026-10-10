using System.Collections.Concurrent;
using Core.Implementations.Protocol;
using Core.Interfaces.Manager;
using Core.Interfaces.Protocol;
using Core.Models.Credentials;

namespace Tests.Fakes;

public class FakeConnectionManager : IConnectionManager
{
    private readonly ConcurrentDictionary<Guid, Connection> _connections = new();
    public Guid CreateConnection(SavedProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var connection = new LocalConnection(Directory.CreateTempSubdirectory().FullName);
        _connections[connection.Id] = connection;
        return connection.Id;
    }

    public List<Guid> GetConnectionIdList()
    {
        return  _connections.Keys.ToList();
    }

    public Connection GetConnection(Guid id)
    {
        if (!_connections.TryGetValue(id, out var connection))
            throw new KeyNotFoundException($"Connection {id} was not found.");
        return connection;
    }

    public bool TryGetConnection(Guid id, out Connection? connection)
    {
        if (_connections.TryGetValue(id, out var conn))
        {
            connection = conn;
            return true;
        }

        connection = null;
        return false;
    }

    public void DeleteConnection(Guid id) => DeleteConnectionAsync(id).GetAwaiter().GetResult();

    public async Task DeleteConnectionAsync(Guid id, CancellationToken ct = default)
    {
        if(!_connections.TryRemove(id, out var connection))
            return;
        try
        {
            await connection.DisconnectAsync(ct);
        }
        catch(Exception)
        {
            //
        }
        await connection.DisposeAsync();
    }

    public Task<int> SweepIdleAsync(TimeSpan idleTimeout, CancellationToken ct = default)
    {
        return Task.FromResult(0);
    }
}