using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using FileManagerClient.Models;

namespace FileManagerClient.Terminal;

/// <summary>One bounded, ordered writer and one reader per terminal. No command replay.</summary>
public sealed class TerminalSocket(WebSocket socket) : IDisposable
{
    private readonly Channel<(byte[] Data, WebSocketMessageType Type)> _outgoing =
        Channel.CreateBounded<(byte[], WebSocketMessageType)>(64);
    private bool _disposed;

    public void SendInput(ReadOnlyMemory<byte> data)
    {
        while (!data.IsEmpty)
        {
            var count = Math.Min(data.Length, 8192);
            Enqueue(data[..count].ToArray(), WebSocketMessageType.Binary);
            data = data[count..];
        }
    }

    public void Resize(TerminalSize size) => Enqueue(
        JsonSerializer.SerializeToUtf8Bytes(size, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
        WebSocketMessageType.Text);

    private void Enqueue(byte[] data, WebSocketMessageType type)
    {
        if (_disposed) return;
        if (!_outgoing.Writer.TryWrite((data, type)))
        {
            Dispose();
            throw new IOException("Очередь ввода переполнена. Откройте новый сеанс терминала.");
        }
    }

    public async Task RunAsync(Func<string, Task> output, CancellationToken ct)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var reader = ReadAsync(output, lifetime.Token);
        var writer = WriteAsync(lifetime.Token);
        try { await await Task.WhenAny(reader, writer); }
        finally
        {
            await lifetime.CancelAsync();
            Dispose();
            try { await Task.WhenAll(reader, writer); }
            catch (Exception) { /* Both tasks observed; the first failure is propagated above. */ }
        }
    }

    private async Task ReadAsync(Func<string, Task> output, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var chars = new char[Encoding.UTF8.GetMaxCharCount(buffer.Length)];
        var decoder = Encoding.UTF8.GetDecoder();
        while (true)
        {
            var frame = await socket.ReceiveAsync(buffer.AsMemory(), ct);
            if (frame.MessageType == WebSocketMessageType.Close)
            {
                if (socket.CloseStatus != WebSocketCloseStatus.NormalClosure)
                    throw new IOException("Соединение с терминалом прервано.");
                return;
            }
            if (frame.MessageType != WebSocketMessageType.Binary)
                throw new IOException("Неподдерживаемый ответ терминала.");
            // Keep incomplete UTF-8 characters across SSH/WebSocket frame boundaries.
            var count = decoder.GetChars(buffer, 0, frame.Count, chars, 0, flush: false);
            if (count > 0) await output(new string(chars, 0, count));
        }
    }

    private async Task WriteAsync(CancellationToken ct)
    {
        await foreach (var item in _outgoing.Reader.ReadAllAsync(ct))
            await socket.SendAsync(item.Data.AsMemory(), item.Type, true, ct);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _outgoing.Writer.TryComplete();
        socket.Abort();
        socket.Dispose();
    }
}
