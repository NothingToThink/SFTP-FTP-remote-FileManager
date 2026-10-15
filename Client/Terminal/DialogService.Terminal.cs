using FileManagerClient.Api;
using FileManagerClient.Terminal;

namespace FileManagerClient.Services;

public partial class DialogService
{
    public void ShowTerminalWindow(FileManagerApiClient api, Guid connectionId, string name)
    {
        if (Owner is not null)
            new TerminalWindow(api, connectionId, name).Show(Owner);
    }
}
