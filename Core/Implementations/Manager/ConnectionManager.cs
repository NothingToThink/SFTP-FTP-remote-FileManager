using System.Collections.Concurrent;

using Core.Interfaces.Manager;
using Core.Interfaces.Storage;
using Core.Interfaces.Protocol;
using Core.Interfaces.Factory;
using Core.Models;
using Core.Models.Credentials;

namespace Core.Implementations.Manager;

public class ConnectionManager : IConnectionManager 
{
    private static ConnectionManager? _instance;
    private static readonly SemaphoreSlim _lock = new(1, 1);
    private IProfileStorage _storage;
    private IConnectionFactory _connectionFactory;
    private ConcurrentDictionary<HostProfile, Connection> _hostProfileToConnection = new();
    private Task Initialization { get; }
    private ConnectionManager (IProfileStorage storage, IConnectionFactory connectionFactory)
    {
        _storage = storage;
        _connectionFactory = connectionFactory;
        Initialization = InitializeAsync();
    }

    public static async Task<ConnectionManager> CreateAsync (IProfileStorage storage, IConnectionFactory connectionFactory) {
        if (_instance != null) {
            return _instance;
        }
        await _lock.WaitAsync();
        try
        {
            if (_instance == null)
            {
                var manager = new ConnectionManager(storage, connectionFactory);
                await manager.Initialization;
                _instance = manager;
            }
        }
        finally
        {
            _lock.Release();
        }

        return _instance;
    }
    public async Task CreateConnection (HostProfile profile)
    {
        try 
        {
            await _storage.Save(profile);
        }
        catch (Exception e)
        {
            throw new InvalidOperationException($"IProfileStorage.GetProfiles failed, connection not created", e);
        }

        await AddConnection(profile);
    }
    public Task<IReadOnlyList<HostProfile>> GetProfilesList()
    {
        //Todo HostProfile <- Iconncetion
        return Task.FromResult<IReadOnlyList<HostProfile>>(_hostProfileToConnection.Keys.ToList());
    }
    public Task<IConnection> GetConnection(HostProfile profile)
    {
        if (!_hostProfileToConnection.TryGetValue(profile, out IConnection? connection))
        {
            throw new KeyNotFoundException($"connection not exists");
        }
        return Task.FromResult(connection);
    }

    private async Task DeleteConnection(HostProfile profile)
    {
        //TODO по id
        try
        {
            await _storage.Delete(profile.Name);
        }
        catch(Exception e)
        {
            throw new InvalidOperationException("IProfileStorage.Delete failed", e);
        }
    }

    private async Task InitializeAsync()
    {
        List<HostProfile> profiles;
        try 
        {
            profiles = await _storage.GetProfiles();
        }
        catch (Exception e)
        {
            throw new InvalidOperationException("IProfileStorage.GetProfiles failed", e);
        }

        foreach (var profile in profiles)
        {
            await AddConnection(profile);
        }
    }

    private async Task AddConnection(HostProfile profile)
    {
        var newConnection = _connectionFactory.CreateConnection(profile);
        _hostProfileToConnection[profile] = newConnection;
    }
}
