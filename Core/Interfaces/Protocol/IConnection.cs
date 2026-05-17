using Core.Models;

namespace Core.Interfaces.Protocol;

public interface IConnection : IQuery, ICommand
{
    Task<OperationStatus> Connect(HostProfile profile);
    Task<OperationStatus> Disconnect();
    bool IsConnected { get; }
}
