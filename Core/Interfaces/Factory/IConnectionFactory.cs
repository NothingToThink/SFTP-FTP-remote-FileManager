using Core.Interfaces.Protocol;
using Core.Models.Credentials;

namespace Core.Interfaces.Factory;

public interface IConnectionFactory
{
    Connection CreateConnection(HostProfile profile);
}