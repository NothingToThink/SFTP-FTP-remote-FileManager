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
    private ConnectionManager (IProfileStorage storage, IConnectionFactory connectionFactory)
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
    public void CreateConnection (SavedProfile profile)
    {
        try
        {
            _storage.Save(new SavedProfile.Create);
        }
        catch (Exception e)
        {
            throw new InvalidOperationException($"IProfileStorage.GetProfiles failed, connection not created", e);
        }

        AddConnection(profile.HostProfile);
    }
    public IReadOnlyList<HostProfile> GetProfilesList()
    {
        //Todo HostProfile <- Iconncetion
        return _connections.Values.ToList();
    }
    public Connection GetConnection(Guid id)
    {
        if (!_hostProfileToConnection.TryGetValue(profile, out Connection? connection))
        {
            throw new KeyNotFoundException($"connection not exists");
        }
        return Task.FromResult(connection);
    }

    private void DeleteConnection(HostProfile profile)
    {
        //TODO по id
        try
        {
            _storage.Delete(profile.Name);
        }
        catch(Exception e)
        {
            throw new InvalidOperationException("IProfileStorage.Delete failed", e);
        }
    }

    private void AddConnection(HostProfile profile)
    {
        var newConnection = _connectionFactory.CreateConnection(profile);
        _connections[newConnection.Id] = newConnection;
    }
}
