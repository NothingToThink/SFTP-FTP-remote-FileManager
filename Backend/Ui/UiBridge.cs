using FileManager.Plugins;
using Backend.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Backend.Ui;

public interface IUiBridge
{
    /// <returns>The pressed button, or null if the dialog was dismissed.</returns>
    /// <exception cref="UiUnavailableException">Client is not connected or went away while waiting.</exception>
    Task<string?> ShowMessageAsync(string sessionId, MessageSeverity severity, string message,
        IReadOnlyList<string> buttons, CancellationToken ct);

    /// <returns>The entered text, or null if the dialog was dismissed.</returns>
    /// <exception cref="UiUnavailableException">Client is not connected or went away while waiting.</exception>
    Task<string?> ShowInputBoxAsync(string sessionId, InputBoxRequest request, CancellationToken ct);
}

public class UiBridge(IHubContext<UiHub> hub, IUiSessionRegistry registry, ILogger<UiBridge> logger) : IUiBridge
{
    public Task<string?> ShowMessageAsync(string sessionId, MessageSeverity severity, string message,
        IReadOnlyList<string> buttons, CancellationToken ct) =>
        InvokeAsync(sessionId, "ShowMessage", new ShowMessageRequest(severity, message, buttons), ct);

    public Task<string?> ShowInputBoxAsync(string sessionId, InputBoxRequest request, CancellationToken ct) =>
        InvokeAsync(sessionId, "ShowInputBox", request, ct);

    private async Task<string?> InvokeAsync(string sessionId, string method, object argument, CancellationToken ct)
    {
        using var disconnected = CancellationTokenSource.CreateLinkedTokenSource(ct);

        void OnDisconnected(string id)
        {
            if (id != sessionId)
                return;
            try
            {
                disconnected.Cancel();
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Cancelling UI call {Method} failed.", method);
            }
        }

        // Subscribe before checking IsConnected: a disconnect between the two cannot be missed.
        registry.ClientDisconnected += OnDisconnected;
        try
        {
            if (!registry.IsConnected(sessionId))
                throw new UiUnavailableException($"UI client '{sessionId}' is not connected.");

            // Call the client from a service, not from a hub method: waiting for the answer inside a
            // hub method would block that connection.
            return await hub.Clients.Client(sessionId)
                .InvokeAsync<string?>(method, argument, disconnected.Token);
        }
        catch (Exception e) when (e is OperationCanceledException or HubException or IOException)
        {
            // SignalR reports a cancelled call as HubException ("Invocation canceled by the server"),
            // a dropped connection as IOException or OperationCanceledException.
            ct.ThrowIfCancellationRequested();

            // Not cancelled by the caller: the client went away or cannot handle the method
            // (e.g. no handler registered). Not fatal for the Backend.
            logger.LogWarning(e, "UI call {Method} to '{SessionId}' failed.", method, sessionId);
            throw new UiUnavailableException($"UI client '{sessionId}' is unavailable for {method}: {e.Message}", e);
        }
        finally
        {
            registry.ClientDisconnected -= OnDisconnected;
        }
    }
}
