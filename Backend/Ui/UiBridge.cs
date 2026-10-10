using Backend.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Backend.Ui;

public class UiBridge(IHubContext<UiHub> hub, IUiSessionRegistry registry) : IUiBridge
{
    public Task<string?> ShowMessageAsync(string sessionId, UiSeverity severity, string message,
        IReadOnlyList<string> buttons, CancellationToken ct = default) =>
        InvokeAsync(sessionId, "ShowMessage", new UiMessageRequest(severity, message, buttons.ToArray()), ct);

    public Task<string?> ShowInputBoxAsync(string sessionId, InputBoxRequest request, CancellationToken ct = default) =>
        InvokeAsync(sessionId, "ShowInputBox", request, ct);

    private async Task<string?> InvokeAsync(string sessionId, string method, object argument, CancellationToken ct)
    {
        using var disconnected = CancellationTokenSource.CreateLinkedTokenSource(ct);

        void OnDisconnected(string id)
        {
            if (id == sessionId)
                disconnected.Cancel();
        }

        // Subscribe before the check: a disconnect in between must not be lost.
        registry.Disconnected += OnDisconnected;
        try
        {
            if (!registry.IsConnected(sessionId))
                throw new UiUnavailableException($"UI client '{sessionId}' is not connected.");

            try
            {
                return await hub.Clients.Client(sessionId)
                    .InvokeAsync<string?>(method, argument, disconnected.Token);
            }
            catch (Exception e)
            {
                // The caller gave up: SignalR reports that as HubException, normalise to cancellation.
                ct.ThrowIfCancellationRequested();

                // SignalR fails pending client calls with IOException when the connection drops,
                // possibly before the hub's OnDisconnectedAsync has removed the session.
                if (disconnected.IsCancellationRequested || e is IOException || !registry.IsConnected(sessionId))
                    throw new UiUnavailableException($"UI client '{sessionId}' disconnected.", e);

                throw;
            }
        }
        finally
        {
            registry.Disconnected -= OnDisconnected;
        }
    }
}
