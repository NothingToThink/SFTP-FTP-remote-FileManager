using Core.Interfaces.Protocol;
using Core.Models.Credentials;

namespace Core.Interfaces.Manager;

public interface IConnectionManager
{
    Guid CreateConnection(SavedProfile profile);
    List<Guid> GetConnectionIdList();

    /// <summary>Returns the connection and marks it as recently used.</summary>
    Connection GetConnection(Guid id);

    bool TryGetConnection(Guid id, out Connection? connection);

    /// <summary>Disconnects, releases and forgets the connection. Safe to call for an unknown id.</summary>
    void DeleteConnection(Guid id);

    Task DeleteConnectionAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Closes connections idle for longer than <paramref name="idleTimeout"/>. Connections holding
    /// active port forwards are kept. Returns how many were collected.
    /// </summary>
    Task<int> SweepIdleAsync(TimeSpan idleTimeout, CancellationToken ct = default);
}
