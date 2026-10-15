using System.Net.WebSockets;
using FileManagerClient.Models;
using FileManagerClient.Terminal;

namespace FileManagerClient.Api;

public partial class FileManagerApiClient
{
    public async Task<TerminalSocket> OpenTerminalAsync(Guid connectionId, TerminalSize size,
        CancellationToken ct = default)
    {
        var uri = new UriBuilder($"{BaseUrl}/connections/{connectionId}/terminal")
        {
            Scheme = new Uri(BaseUrl).Scheme == "https" ? "wss" : "ws",
            Query = $"columns={size.Columns}&rows={size.Rows}",
        };
        var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await socket.ConnectAsync(uri.Uri, timeout.Token);
            return new TerminalSocket(socket);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>HTTP contract check; non-SSH connections return the usual JSON error.</summary>
    public Task CheckTerminalAsync(Guid connectionId, CancellationToken ct = default) =>
        GetAsync<object>($"connections/{connectionId}/terminal", ct);
}
