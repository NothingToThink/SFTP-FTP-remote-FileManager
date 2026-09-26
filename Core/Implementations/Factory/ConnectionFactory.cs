using Core.Interfaces.Factory;
using Core.Models.Credentials;
using Core.Interfaces.Protocol;
using Core.Implementations.Protocol;
using System.Diagnostics;

namespace Core.Implementations.Factory;

public class ConnectionFactory : IConnectionFactory
{
    public Connection CreateConnection(HostProfile profile)
    {
        switch (profile.Protocol)
        {
            case Models.Protocol.Local:
                return new LocalConnection();
            case Models.Protocol.Ftp:
                return new FtpConnection(profile);
            case Models.Protocol.Sftp:
                return new SftpConnection(profile);
            default:
                throw new UnreachableException($"Unknown protocol: {profile.Protocol}");
        }
    }
}
