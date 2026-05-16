using Core.Interfaces.Factory;
using Core.Models;
using Core.Interfaces.Protocol;
using Core.Implementations.Protocol;

namespace Core.Implementations.Factory;

public class ConnectionFactory : IConnectionFactory
{
    public IMethod CreateMethod(HostProfile profile) {
        //нужно добавить enum в Protocol вместо string
        //и вообще раз у нас IMethod - connection, то стоит в нём получать на вход HostProfile,
        //и не делать IMethod.Connect(HostProfile)
        if (profile.Protocol == "local")
        {
            return new CommandsLocal();
            //return new CommandsLocal(profile);
        }
        if (profile.Protocol == "ftp")
        {
            return new CommandsFtp();
        }
        return new CommandsSftp();
    }
}