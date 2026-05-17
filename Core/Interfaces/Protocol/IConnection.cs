using Core.Models;

namespace Core.Interfaces.Protocol;

public interface IConnection : IQuery, ICommand
{
    Task<OperationStatus> Connect();
    Task<OperationStatus> Disconnect();
    bool IsConnected { get; }
}
