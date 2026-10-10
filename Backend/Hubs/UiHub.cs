using Backend.Ui;
using Microsoft.AspNetCore.SignalR;

namespace Backend.Hubs;

/// <summary>
/// Backend -> client channel for dialogs. The hub has no methods on purpose: waiting for a client's
/// answer inside a hub method would block that client's connection. Calls go out through
/// <see cref="IUiBridge"/> via <c>IHubContext&lt;UiHub&gt;</c>.
/// </summary>
public class UiHub(IUiSessionRegistry registry) : Hub
{
    public override Task OnConnectedAsync()
    {
        registry.Add(Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        registry.Remove(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
