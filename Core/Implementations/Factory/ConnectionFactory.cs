using Core.Interfaces.Factory;
using Core.Models;
using Core.Interfaces.Protocol;
using Core.Implementations.Protocol;

namespace Core.Implementations.Factory;

public class ConnectionFactory : IConnectionFactory
{
    public IConnection CreateConnection(HostProfile profile)
    {
        //нужно добавить enum в Protocol вместо string
        //и вообще раз у нас IConnection - connection, то стоит в нём получать на вход HostProfile,
        //и не делать IConnection.Connect(HostProfile)
        switch (profile.Protocol.ToLower())
        {
            case "local":
                return new LocalConnection();
            case "ftp":
                return new FtpConnection(profile);
            case "sftp":
                return new SftpConnection(profile);
            default:
                throw new ArgumentOutOfRangeException($"Unknown protocol: {profile.Protocol}");
        }
    }
}
