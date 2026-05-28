using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Core.Interfaces.Manager;
using Core.Interfaces.Protocol;
using Core.Models.Credentials;

namespace Tests.Fakes;

public class FakeConnectionManager : IConnectionManager
{
    private readonly ConcurrentDictionary<Guid, FakeConnection> _connections = new();
    public Guid CreateConnection(SavedProfile profile)
    {
        var id = Guid.NewGuid();
        _connections[id] = new FakeConnection();
        return id;
    }

    public List<Guid> GetConnectionIdList()
    {
        return  _connections.Keys.ToList();
    }

    public Connection GetConnection(Guid id)
    {
        return _connections.TryGetValue(id, out var connection)
            ? connection
            : throw new KeyNotFoundException($"Connection {id} not found");
    }

    public void DeleteConnection(Guid id)
    {
        _connections.TryRemove(id, out _);
    }
}