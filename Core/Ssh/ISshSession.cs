using Renci.SshNet;

namespace Core.Ssh;

/// <summary>
/// An authenticated SSH transport to one host, shared by everything that needs it:
/// the SFTP file browser, remote command execution, and port forwarding.
/// </summary>
public interface ISshSession : IAsyncDisposable
{
    Guid Id { get; }
    string Host { get; }
    int Port { get; }
    string Username { get; }

    bool IsConnected { get; }
    DateTimeOffset LastUsedAtUtc { get; }

    /// <summary>Number of port forwards currently held open on this session.</summary>
    int ActiveForwardCount { get; }

    /// <summary>Establishes the transport if it is not up yet. Safe to call concurrently.</summary>
    Task ConnectAsync(CancellationToken ct = default);

    Task DisconnectAsync(CancellationToken ct = default);

    /// <summary>The SFTP channel, connected on first use.</summary>
    Task<SftpClient> GetSftpAsync(CancellationToken ct = default);

    /// <summary>The shell/exec channel, connected on first use. Also owns forwarded ports.</summary>
    Task<SshClient> GetSshAsync(CancellationToken ct = default);

    /// <summary>Marks the session as in use, deferring idle collection.</summary>
    void Touch();

    /// <summary>
    /// Records that a port forward is holding this session open. Paired with
    /// <see cref="UnregisterForward"/>; callers other than the forwarding manager should not use these.
    /// </summary>
    void RegisterForward();

    void UnregisterForward();
}
