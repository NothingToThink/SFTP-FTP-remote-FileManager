
using Core.Models;
using Core.Models.Credentials;

namespace Core.Interfaces.UI;

public interface IConnectionUiService
{
    Task<OperationStatus> Connect(HostProfile profile);
    Task<OperationStatus> TestConnection(HostProfile profile);
    Task Disconnect();
    bool IsConnected { get; }
    event Action<bool>? ConnectionStateChanged;
}