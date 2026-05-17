using Core.Models;
using Core.Interfaces.Protocol;

namespace Core.Interfaces.Connection;

public interface IConnectionManager
{
    Task CreateConnection(HostProfile profile);
    Task<IReadOnlyList<HostProfile>> GetProfilesList();
    Task<IConnection> GetConnection(HostProfile profile);
}