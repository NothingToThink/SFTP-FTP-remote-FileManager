using Core.Models.Credentials;
using Core.Interfaces.Protocol;

namespace Core.Interfaces.Manager;

public interface IConnectionManager
{
    Task CreateConnection(HostProfile profile);
    Task<IReadOnlyList<HostProfile>> GetProfilesList();
    Task<Connection> GetConnection(HostProfile profile);
}