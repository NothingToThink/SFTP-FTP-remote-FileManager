using Core.Interfaces.Protocol;
using Core.Models;
using Core.Models.Credentials;

namespace Core.Interfaces.Factory;

public interface IRouterFactory
{
    IConnection CreateMethod(HostProfile profile);
}