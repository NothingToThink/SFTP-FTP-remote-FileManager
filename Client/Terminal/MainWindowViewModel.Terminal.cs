using CommunityToolkit.Mvvm.Input;
using FileManagerClient.Models;

namespace FileManagerClient.ViewModels;

public partial class MainWindowViewModel
{
    public bool IsTerminalAvailable => Browser.IsBound && Browser.BoundProtocol == Protocol.Sftp;

    [RelayCommand]
    private void OpenTerminal()
    {
        if (IsTerminalAvailable)
            _dialogs.ShowTerminalWindow(Api, Browser.ConnectionId, Browser.ConnectionName);
    }
}
