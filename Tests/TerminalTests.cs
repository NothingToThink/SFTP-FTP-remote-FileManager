using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using Core.Implementations.Protocol;
using Core.Interfaces.Manager;
using Core.Ssh;
using Core.Terminal;
using FileManagerClient.Terminal;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Renci.SshNet;
using Tests.Fakes;

namespace Tests;

public sealed class TerminalTests
{
    [Fact]
    public async Task HttpContract_RejectsMissingLocalInvalidSizeAndBrowserOrigin()
    {
        var manager = new FakeConnectionManager();
        using var local = new LocalConnection(Directory.CreateTempSubdirectory().FullName);
        var id = manager.Add(local);
        await using var factory = CreateFactory(manager, new FakeTerminalFactory());
        using var http = factory.CreateClient();
        foreach (var (path, code) in new[]
        {
            ($"connections/{Guid.NewGuid()}/terminal", HttpStatusCode.NotFound),
            ($"connections/{id}/terminal", HttpStatusCode.BadRequest),
            ($"connections/{id}/terminal?columns=501", HttpStatusCode.BadRequest),
            ($"connections/{id}/terminal?rows=0", HttpStatusCode.BadRequest),
        })
        {
            using var response = await http.GetAsync(path);
            Assert.Equal(code, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
            Assert.False(string.IsNullOrWhiteSpace(body!["error"]));
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, $"connections/{id}/terminal");
        request.Headers.Add("Origin", "https://example.org");
        using var forbidden = await http.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task WebSocket_PreservesBytes_Resizes_AndClosesOnlyShell()
    {
        var manager = new FakeConnectionManager();
        var session = new FakeSshSession();
        var id = manager.Add(new SftpConnection(session));
        var terminals = new FakeTerminalFactory();
        await using var factory = CreateFactory(manager, terminals);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var ws = await factory.Server.CreateWebSocketClient().ConnectAsync(
            new Uri($"ws://localhost/connections/{id}/terminal?columns=120&rows=40"), deadline.Token);
        Assert.Equal((120, 40), terminals.InitialSize);
        var bytes = Encoding.UTF8.GetBytes("cd /tmp\rПривет\t\u0003\u001b[A");
        // Fragment in the middle of a multibyte character; the transport must not decode input.
        await ws.SendAsync(bytes.AsMemory(0, 11), WebSocketMessageType.Binary, false, deadline.Token);
        await ws.SendAsync(bytes.AsMemory(11), WebSocketMessageType.Binary, true, deadline.Token);
        var received = new byte[bytes.Length];
        var count = 0;
        while (count < received.Length)
        {
            var frame = await ws.ReceiveAsync(received.AsMemory(count), deadline.Token);
            Assert.Equal(WebSocketMessageType.Binary, frame.MessageType);
            count += frame.Count;
        }
        Assert.Equal(bytes, received);
        await ws.SendAsync(Encoding.UTF8.GetBytes("{\"columns\":88,\"rows\":25}"),
            WebSocketMessageType.Text, true, deadline.Token);
        Assert.Equal((88, 25), await terminals.Terminal.Resized.Task.WaitAsync(deadline.Token));
        Assert.True(session.Touches > 0);
        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", deadline.Token);
        await terminals.Terminal.Disposed.Task.WaitAsync(deadline.Token);
        Assert.False(session.WasDisposed);
        Assert.True(session.IsConnected);
    }

    [Fact]
    public async Task WebSocket_ShellEof_ClosesNormally_AndDisposesChannel()
    {
        var manager = new FakeConnectionManager();
        var id = manager.Add(new SftpConnection(new FakeSshSession()));
        var terminals = new FakeTerminalFactory();
        await using var factory = CreateFactory(manager, terminals);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var ws = await factory.Server.CreateWebSocketClient().ConnectAsync(
            new Uri($"ws://localhost/connections/{id}/terminal"), deadline.Token);
        terminals.Terminal.Output.Complete();
        var frame = await ws.ReceiveAsync(new byte[100].AsMemory(), deadline.Token);
        Assert.Equal(WebSocketMessageType.Close, frame.MessageType);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, ws.CloseStatus);
        await terminals.Terminal.Disposed.Task.WaitAsync(deadline.Token);
    }

    [Fact]
    public async Task WebSocket_TransportDisconnect_UnblocksPendingShellRead()
    {
        var manager = new FakeConnectionManager();
        var id = manager.Add(new SftpConnection(new FakeSshSession()));
        var terminals = new FakeTerminalFactory();
        await using var factory = CreateFactory(manager, terminals);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var ws = await factory.Server.CreateWebSocketClient().ConnectAsync(
            new Uri($"ws://localhost/connections/{id}/terminal"), deadline.Token);
        terminals.Terminal.IsConnected = false;
        await terminals.Terminal.Disposed.Task.WaitAsync(deadline.Token);
    }

    [Fact]
    public async Task ClientSocket_DecodesSplitUtf8_AndPreservesLiteralEscapes()
    {
        var manager = new FakeConnectionManager();
        var id = manager.Add(new SftpConnection(new FakeSshSession()));
        var terminals = new FakeTerminalFactory();
        await using var factory = CreateFactory(manager, terminals);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var ws = await factory.Server.CreateWebSocketClient().ConnectAsync(
            new Uri($"ws://localhost/connections/{id}/terminal"), deadline.Token);
        using var client = new TerminalSocket(ws);
        var received = new StringBuilder();
        var text = "Привет, мир! Ёжик №1 — файл.txt 😀\\u041f\\320\\237";
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = client.RunAsync(chunk =>
        {
            received.Append(chunk);
            if (received.Length >= text.Length) complete.TrySetResult();
            return Task.CompletedTask;
        }, deadline.Token);
        // Split every multibyte character, including a surrogate pair, over separate frames.
        foreach (var value in Encoding.UTF8.GetBytes(text))
            await terminals.Terminal.Output.WriteAsync(new[] { value }, deadline.Token);
        await complete.Task.WaitAsync(deadline.Token);
        terminals.Terminal.Output.Complete();
        await run.WaitAsync(deadline.Token);
        Assert.Equal(text, received.ToString());
    }

    private static WebApplicationFactory<Program> CreateFactory(FakeConnectionManager manager,
        FakeTerminalFactory terminals) => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IConnectionManager>();
            services.AddSingleton<IConnectionManager>(manager);
            services.RemoveAll<ITerminalSessionFactory>();
            services.AddSingleton<ITerminalSessionFactory>(terminals);
        }));

    private sealed class FakeTerminalFactory : ITerminalSessionFactory
    {
        public FakeTerminal Terminal { get; } = new();
        public (int, int) InitialSize { get; private set; }
        public Task<ITerminalSession> OpenAsync(ISshSession session, int columns, int rows, CancellationToken ct)
        {
            InitialSize = (columns, rows);
            return Task.FromResult<ITerminalSession>(Terminal);
        }
    }

    private sealed class FakeTerminal : ITerminalSession
    {
        public EchoStream Output { get; } = new();
        public Stream Stream => Output;
        public bool IsConnected { get; set; } = true;
        public TaskCompletionSource<(int, int)> Resized { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task ResizeAsync(int columns, int rows, CancellationToken ct)
        {
            Resized.TrySetResult((columns, rows));
            return Task.CompletedTask;
        }
        public void Dispose() { Output.Complete(); Disposed.TrySetResult(); }
    }

    private sealed class EchoStream : Stream
    {
        private readonly Channel<byte[]> _output = Channel.CreateUnbounded<byte[]>();
        private ReadOnlyMemory<byte> _pending;
        public void Complete() => _output.Writer.TryComplete();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_pending.IsEmpty)
            {
                if (!await _output.Reader.WaitToReadAsync(ct)) return 0;
                _pending = await _output.Reader.ReadAsync(ct);
            }
            var count = Math.Min(buffer.Length, _pending.Length);
            _pending[..count].CopyTo(buffer);
            _pending = _pending[count..];
            return count;
        }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) =>
            _output.Writer.WriteAsync(buffer.ToArray(), ct);
        public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;
        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private sealed class FakeSshSession : ISshSession
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string Host => "test";
        public int Port => 22;
        public string Username => "test";
        public bool IsConnected => !WasDisposed;
        public DateTimeOffset LastUsedAtUtc => DateTimeOffset.UtcNow;
        public int ActiveForwardCount => 0;
        public int Touches { get; private set; }
        public bool WasDisposed { get; private set; }
        public void Touch() => Touches++;
        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<SftpClient> GetSftpAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SshClient> GetSshAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public void RegisterForward() => throw new NotSupportedException();
        public void UnregisterForward() => throw new NotSupportedException();
        public ValueTask DisposeAsync() { WasDisposed = true; return ValueTask.CompletedTask; }
    }
}
