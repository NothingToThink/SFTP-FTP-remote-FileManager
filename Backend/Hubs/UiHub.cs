using Backend.Ui;
using Microsoft.AspNetCore.SignalR;

namespace Backend.Hubs;

/// <summary>
/// Channel Backend -> client for dialogs. The hub deliberately has no methods: the Backend calls the client
/// through <see cref="IUiBridge"/> (IHubContext), because awaiting a client's answer inside a hub method
/// would block that connection's message loop.
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
