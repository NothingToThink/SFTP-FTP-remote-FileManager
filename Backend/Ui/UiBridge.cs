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
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new UiUnavailableException($"UI client '{sessionId}' disconnected.");
            }
            catch (Exception e) when (e is not OperationCanceledException && !registry.IsConnected(sessionId))
            {
                // SignalR fails pending client calls when the connection drops.
                throw new UiUnavailableException($"UI client '{sessionId}' disconnected.", e);
            }
        }
        finally
        {
            registry.Disconnected -= OnDisconnected;
        }
    }
}
