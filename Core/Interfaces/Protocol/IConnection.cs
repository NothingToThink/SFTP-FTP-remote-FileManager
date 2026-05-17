using Core.Models;
using Core.Models.Credentials;

namespace Core.Interfaces.Protocol;

public interface IConnection : IQuery, ICommand, IDisposable
{
    Task<OperationStatus> Connect(HostProfile profile);
    Task<OperationStatus> Disconnect();
    bool IsConnected { get; }
}