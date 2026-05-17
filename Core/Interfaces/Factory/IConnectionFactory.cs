using Core.Interfaces.Protocol;
using Core.Models;

namespace Core.Interfaces.Factory;

public interface IConnectionFactory
{
    IConnection CreateConnection(HostProfile profile);
}
