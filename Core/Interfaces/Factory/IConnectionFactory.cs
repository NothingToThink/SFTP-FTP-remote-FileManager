using Core.Interfaces.Protocol;
using Core.Models;

namespace Core.Interfaces.Factory;

public interface IConnectionFactory
{
    IMethod CreateMethod(HostProfile profile);
}