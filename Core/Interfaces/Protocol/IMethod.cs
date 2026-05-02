using Core.Models;

namespace Core.Interfaces.Protocol;

public interface IMethod : IQuery, ICommand
{
    Task<OperationStatus> Connect(HostProfile profile);
    Task<OperationStatus> Disconnect();
    bool IsConnected { get; }
}