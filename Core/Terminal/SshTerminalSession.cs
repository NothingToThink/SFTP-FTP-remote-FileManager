using Core.Ssh;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Core.Terminal;

public interface ITerminalSession : IDisposable
{
    Stream Stream { get; }
    bool IsConnected { get; }
    Task ResizeAsync(int columns, int rows, CancellationToken ct);
}

public interface ITerminalSessionFactory
{
    Task<ITerminalSession> OpenAsync(ISshSession session, int columns, int rows, CancellationToken ct);
}

/// <summary>Owns only a PTY shell channel; the existing SSH transport remains shared.</summary>
public sealed class SshTerminalSessionFactory : ITerminalSessionFactory
{
    public async Task<ITerminalSession> OpenAsync(ISshSession session, int columns, int rows,
        CancellationToken ct)
    {
        var client = await session.GetSshAsync(ct);
        // SSH.NET exposes PTY creation only synchronously. Keep it off the request/UI thread.
        var shell = await Task.Run(() => client.CreateShellStream(
            "xterm-256color", (uint)columns, (uint)rows, 0, 0, 8192,
            new Dictionary<TerminalModes, uint> { [TerminalModes.IUTF8] = 1 }), ct);
        if (ct.IsCancellationRequested)
        {
            shell.Dispose();
            ct.ThrowIfCancellationRequested();
        }
        return new SshTerminalSession(client, shell);
    }

    private sealed class SshTerminalSession(SshClient client, ShellStream shell) : ITerminalSession
    {
        private int _disposed;
        public Stream Stream => shell;
        public bool IsConnected => Volatile.Read(ref _disposed) == 0 && client.IsConnected;
        public Task ResizeAsync(int columns, int rows, CancellationToken ct) =>
            Task.Run(() => shell.ChangeWindowSize((uint)columns, (uint)rows, 0, 0), ct);
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                shell.Dispose();
        }
    }
}
