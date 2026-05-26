using Core.Interfaces.Manager;
using Core.Interfaces.Protocol;
using Core.Interfaces.Factory;
using Core.Models.Credentials;

namespace Core.Implementations.Manager;

public class ConnectionManager (IConnectionFactory connectionFactory) : IConnectionManager 
{
    private readonly IConnectionFactory _connectionFactory = connectionFactory;
    private Dictionary<Guid, Connection> _connections = new();

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
        return _connections[id] ?? throw new InvalidOperationException($"Connection with id = {id} doesnt exists");
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
