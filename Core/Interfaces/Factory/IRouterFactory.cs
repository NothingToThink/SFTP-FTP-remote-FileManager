using Core.Interfaces.Protocol;
using Core.Models;

namespace Core.Interfaces.Factory;

public interface IRouterFactory
{
    IConnection CreateMethod(HostProfile profile);
}