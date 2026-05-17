using Core.Interfaces.Factory;
using Core.Models;
using Core.Interfaces.Protocol;
using Core.Implementations.Protocol;
using System.Diagnostics;

namespace Core.Implementations.Factory;

public class ConnectionFactory : IConnectionFactory
{
    public IConnection CreateConnection(HostProfile profile)
    {
        switch (profile.Protocol)
        {
            case ProtocolName.local:
                return new LocalConnection();
            case ProtocolName.ftp:
                return new FtpConnection(profile);
            case ProtocolName.sftp:
                return new SftpConnection(profile);
            default:
                throw new UnreachableException($"Unknown protocol: {profile.Protocol}");
        }
    }
}
