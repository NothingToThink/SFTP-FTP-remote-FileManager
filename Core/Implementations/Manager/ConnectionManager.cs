using System.Collections.Concurrent;

using Core.Interfaces.Manager;
using Core.Interfaces.Storage;
using Core.Interfaces.Protocol;
using Core.Interfaces.Factory;
using Core.Models.Credentials;

namespace Core.Implementations.Manager;

public class ConnectionManager : IConnectionManager 
{
    private readonly IProfileStorage _storage;
    private readonly IConnectionFactory _connectionFactory;
    private Dictionary<Guid, Connection> _connections = new();
    public ConnectionManager (IProfileStorage storage, IConnectionFactory connectionFactory)
    {
        _storage = storage;
        _connectionFactory = connectionFactory;

        List<HostProfile> profiles;
        try 
        {
            var storageProfiles = _storage.GetProfiles();
            profiles = storageProfiles.Select(p => p.HostProfile with{}).ToList();
        }
        catch (Exception e)
        {
            throw new InvalidOperationException("IProfileStorage.GetProfiles failed", e);
        }

        foreach (var profile in profiles)
        {
            AddConnection(profile);
        }
    }
    public Guid CreateConnection (SavedProfile profile)
    {
        return AddConnection(profile.HostProfile);
    }
    public List<Guid> GetConnectionIdList()
    {
        return _connections.Keys.ToList();
    }
    
    public Connection GetConnection (Guid id)
    {
        return _connections[id] ?? throw new InvalidOperationException("Current connection is not set");
    }

    public List<SavedProfile> GetProfilesList()
    {
        return _storage.GetProfiles();
    }

    public void DeleteConnection (Guid id)
    {
        _connections.Remove(id);
    }

    private Guid AddConnection(HostProfile profile)
    {
        var newConnection = _connectionFactory.CreateConnection(profile);
        _connections[newConnection.Id] = newConnection;
        return newConnection.Id;
    }
}
