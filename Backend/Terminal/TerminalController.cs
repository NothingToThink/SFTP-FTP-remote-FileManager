using Core.Interfaces.Manager;
using Core.Ssh;
using Core.Terminal;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Terminal;

[ApiController]
[Route("connections/{connectionId:guid}/terminal")]
public sealed class TerminalController(IConnectionManager connections, ITerminalSessionFactory terminals)
    : ControllerBase
{
    [HttpGet]
    public async Task Open(Guid connectionId, CancellationToken ct, int columns = 100, int rows = 30)
    {
        // The desktop client sends no Origin. Reject browser-originated shell connections.
        if (Request.Headers.ContainsKey("Origin"))
        {
            Response.StatusCode = 403;
            await Response.WriteAsJsonAsync(new { error = "Терминал доступен из настольного клиента." }, ct);
            return;
        }
        TerminalBridge.ValidateSize(columns, rows);
        var connection = connections.GetConnection(connectionId);
        if (connection is not ISshSessionProvider provider)
            throw new InvalidOperationException("Терминал требует SFTP/SSH-подключения.");
        if (!provider.Session.IsConnected)
            throw new InvalidOperationException("Сначала подключитесь к серверу.");
        if (!HttpContext.WebSockets.IsWebSocketRequest)
            throw new InvalidOperationException("Терминал требует WebSocket-подключения.");

        // Authentication and host-key errors still pass through the existing HTTP middleware.
        using var terminal = await terminals.OpenAsync(provider.Session, columns, rows, ct);
        using var socket = await HttpContext.WebSockets.AcceptWebSocketAsync();
        await TerminalBridge.RunAsync(socket, terminal, provider.Session.Touch, ct);
    }
}
