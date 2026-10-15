using System.Net.WebSockets;
using System.Text.Json;
using Core.Terminal;

namespace Backend.Terminal;

/// <summary>Binary frames carry raw PTY bytes; text frames carry bounded resize messages.</summary>
public static class TerminalBridge
{
    public static void ValidateSize(int columns, int rows)
    {
        if (columns is < 2 or > 500 || rows is < 1 or > 300)
            throw new InvalidOperationException("Размер терминала: 2–500 столбцов, 1–300 строк.");
    }

    public static async Task RunAsync(WebSocket socket, ITerminalSession terminal, Action touch,
        CancellationToken ct)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // Disposing interrupts SSH.NET reads even when the underlying stream ignores cancellation.
        using var registration = lifetime.Token.Register(terminal.Dispose);
        var input = ReceiveAsync(socket, terminal, lifetime.Token);
        var output = SendAsync(socket, terminal.Stream, lifetime.Token);
        var heartbeat = KeepAliveAsync(terminal, touch, lifetime.Token);
        var status = WebSocketCloseStatus.NormalClosure;
        var message = "Сеанс завершён.";
        try
        {
            await await Task.WhenAny(input, output, heartbeat);
        }
        catch (Exception) when (ct.IsCancellationRequested) { }
        catch (Exception)
        {
            status = WebSocketCloseStatus.InternalServerError;
            message = "Соединение с терминалом прервано.";
        }
        finally
        {
            // On shell EOF, send close BEFORE cancelling ReceiveAsync (cancellation aborts a WebSocket).
            if (!output.IsCompleted) await lifetime.CancelAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                    await socket.CloseOutputAsync(status, message, timeout.Token);
            }
            catch (Exception) { socket.Abort(); }
            await lifetime.CancelAsync();
            try { await Task.WhenAll(input, output, heartbeat); }
            catch (Exception) { /* All tasks observed; no HTTP response may be written after upgrade. */ }
        }
    }

    private static async Task SendAsync(WebSocket socket, Stream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        while (true)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(), ct);
            if (count == 0) return;
            await socket.SendAsync(buffer.AsMemory(0, count), WebSocketMessageType.Binary, true, ct);
        }
    }

    private static async Task ReceiveAsync(WebSocket socket, ITerminalSession terminal, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var resize = new byte[256];
        var resizeLength = 0;
        var messageLength = 0;
        while (true)
        {
            var frame = await socket.ReceiveAsync(buffer.AsMemory(), ct);
            if (frame.MessageType == WebSocketMessageType.Close) return;
            messageLength += frame.Count;
            if (messageLength > 65536)
                throw new InvalidOperationException("Слишком большой пакет терминала.");
            if (frame.MessageType == WebSocketMessageType.Binary)
            {
                await terminal.Stream.WriteAsync(buffer.AsMemory(0, frame.Count), ct);
                await terminal.Stream.FlushAsync(ct);
            }
            else
            {
                if (resizeLength + frame.Count > resize.Length)
                    throw new InvalidOperationException("Слишком большой пакет изменения размера.");
                buffer.AsSpan(0, frame.Count).CopyTo(resize.AsSpan(resizeLength));
                resizeLength += frame.Count;
                if (frame.EndOfMessage)
                {
                    using var json = JsonDocument.Parse(resize.AsMemory(0, resizeLength));
                    var columns = json.RootElement.GetProperty("columns").GetInt32();
                    var rows = json.RootElement.GetProperty("rows").GetInt32();
                    ValidateSize(columns, rows);
                    await terminal.ResizeAsync(columns, rows, ct);
                    resizeLength = 0;
                }
            }
            if (frame.EndOfMessage) messageLength = 0;
        }
    }

    private static async Task KeepAliveAsync(ITerminalSession terminal, Action touch, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        do
        {
            // SSH.NET may leave a blocked ShellStream read after transport disconnect.
            if (!terminal.IsConnected) throw new IOException("SSH-подключение закрыто.");
            touch();
        } while (await timer.WaitForNextTickAsync(ct));
    }
}
