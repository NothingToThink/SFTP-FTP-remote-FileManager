using Core.Models.Credentials;
using Core.Ssh.HostKey;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Core.Ssh;

/// <summary>
/// Owns the SSH transport to a single host and hands out channels to it.
///
/// A note on the shape of this class: SSH.NET's <see cref="SftpClient"/> and <see cref="SshClient"/>
/// each open their own TCP connection and run their own authentication — the library does not let
/// SFTP ride as a channel on an existing <see cref="SshClient"/>. So "one session" here means one
/// <see cref="ConnectionInfo"/>, one host key verification policy and one lifetime, shared by both
/// clients, each of which is connected lazily and only if something actually needs it.
/// </summary>
public sealed class SshSession : ISshSession
{
    private readonly ConnectionInfo _connectionInfo;
    private readonly IHostKeyStore _hostKeyStore;
    private readonly SshSessionOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private SftpClient? _sftp;
    private SshClient? _ssh;
    private int _activeForwardCount;
    private long _lastUsedTicks;
    private bool _disposed;

    /// <summary>
    /// Host key rejection surfaces from SSH.NET as a generic connection failure, so the verification
    /// callback records the real reason here and <see cref="ConnectClientAsync"/> rethrows it.
    /// </summary>
    private Exception? _hostKeyFailure;

    public Guid Id { get; } = Guid.NewGuid();
    public string Host => _connectionInfo.Host;
    public int Port => _connectionInfo.Port;
    public string Username => _connectionInfo.Username;

    public bool IsConnected => _sftp?.IsConnected == true || _ssh?.IsConnected == true;

    public DateTimeOffset LastUsedAtUtc =>
        new(Interlocked.Read(ref _lastUsedTicks), TimeSpan.Zero);

    public int ActiveForwardCount => Volatile.Read(ref _activeForwardCount);

    public SshSession(HostProfile profile, IHostKeyStore hostKeyStore, SshSessionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(profile);

        _hostKeyStore = hostKeyStore ?? throw new ArgumentNullException(nameof(hostKeyStore));
        _options = options ?? new SshSessionOptions();
        _connectionInfo = BuildConnectionInfo(profile, _options);
        Touch();
    }

    public void Touch() => Interlocked.Exchange(ref _lastUsedTicks, DateTimeOffset.UtcNow.UtcTicks);

    /// <summary>
    /// Brings up the SFTP channel, which is what the file browser needs. The exec channel is
    /// connected separately by <see cref="GetSshAsync"/> so that a plain file session does not
    /// pay for a second TCP connection and authentication round trip.
    /// </summary>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        await GetSftpAsync(ct);
    }

    public async Task<SftpClient> GetSftpAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Touch();

        if (_sftp is { IsConnected: true })
            return _sftp;

        await _gate.WaitAsync(ct);
        try
        {
            if (_sftp is { IsConnected: true })
                return _sftp;

            _sftp?.Dispose();
            _sftp = new SftpClient(_connectionInfo);
            await ConnectClientAsync(_sftp, ct);
            return _sftp;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SshClient> GetSshAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Touch();

        if (_ssh is { IsConnected: true })
            return _ssh;

        await _gate.WaitAsync(ct);
        try
        {
            if (_ssh is { IsConnected: true })
                return _ssh;

            // Reconnecting drops every forwarded port that lived on the old client, so refuse to
            // silently do it while tunnels are supposed to be up.
            if (_ssh is not null && ActiveForwardCount > 0)
            {
                throw new InvalidOperationException(
                    $"SSH transport to {Host}:{Port} dropped while {ActiveForwardCount} port forward(s) were active. " +
                    "Restart the forwards after reconnecting.");
            }

            _ssh?.Dispose();
            _ssh = new SshClient(_connectionInfo);
            await ConnectClientAsync(_ssh, ct);
            return _ssh;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            DisconnectClient(_sftp);
            DisconnectClient(_ssh);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Called by the port forwarding manager so idle collection can see live tunnels.</summary>
    public void RegisterForward() => Interlocked.Increment(ref _activeForwardCount);

    public void UnregisterForward()
    {
        // Never let a double-stop drive the counter negative — the janitor reads it as "is this
        // session in use", and a negative value would make a busy session look collectable.
        var updated = Interlocked.Decrement(ref _activeForwardCount);
        if (updated < 0)
            Interlocked.CompareExchange(ref _activeForwardCount, 0, updated);
    }

    private async Task ConnectClientAsync(BaseClient client, CancellationToken ct)
    {
        client.HostKeyReceived += OnHostKeyReceived;
        client.KeepAliveInterval = _options.KeepAliveInterval;
        _hostKeyFailure = null;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_options.ConnectTimeout);

        try
        {
            await client.ConnectAsync(timeoutCts.Token);
        }
        catch (Exception) when (_hostKeyFailure is not null)
        {
            throw _hostKeyFailure;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Timed out after {_options.ConnectTimeout.TotalSeconds:0}s connecting to {Host}:{Port}.");
        }
        finally
        {
            client.HostKeyReceived -= OnHostKeyReceived;
        }
    }

    private void OnHostKeyReceived(object? sender, HostKeyEventArgs e)
    {
        var presented = new HostKeyInfo(e.HostKeyName, e.FingerPrintSHA256);
        var verdict = _hostKeyStore.Verify(Host, Port, presented, out var stored);

        switch (verdict)
        {
            case HostKeyVerdict.Trusted:
                e.CanTrust = true;
                return;

            case HostKeyVerdict.Unknown when _options.HostKeyPolicy == HostKeyPolicy.TrustOnFirstUse:
                _hostKeyStore.Trust(Host, Port, presented);
                e.CanTrust = true;
                return;

            case HostKeyVerdict.Unknown:
                _hostKeyFailure = new UnknownHostKeyException(Host, Port, presented);
                e.CanTrust = false;
                return;

            case HostKeyVerdict.Mismatch:
                // A changed key is never auto-trusted, not even under trust-on-first-use.
                _hostKeyFailure = new HostKeyMismatchException(Host, Port, presented, stored!);
                e.CanTrust = false;
                return;

            default:
                e.CanTrust = false;
                return;
        }
    }

    private static void DisconnectClient(BaseClient? client)
    {
        if (client is { IsConnected: true })
            client.Disconnect();
    }

    private static ConnectionInfo BuildConnectionInfo(HostProfile profile, SshSessionOptions options)
    {
        (string username, AuthenticationMethod method) = profile.Auth switch
        {
            PasswordAuth(var user, var password)
                => (user, (AuthenticationMethod)new PasswordAuthenticationMethod(user, password)),

            KeyAuth(var user, var keyPath, var passphrase)
                => (user, (AuthenticationMethod)new PrivateKeyAuthenticationMethod(user, BuildKeySource(keyPath, passphrase))),

            AnonymousAuth
                => throw new InvalidOperationException("Anonymous authentication is not supported by SSH."),

            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile.Auth, "Unsupported authentication method."),
        };

        return new ConnectionInfo(profile.Host, profile.EffectivePort, username, method)
        {
            Timeout = options.ConnectTimeout,
        };
    }

    private static IPrivateKeySource[] BuildKeySource(string keyPath, string? passphrase)
    {
        if (!File.Exists(keyPath))
            throw new InvalidOperationException($"Private key file not found: {keyPath}");

        var key = string.IsNullOrEmpty(passphrase)
            ? new PrivateKeyFile(keyPath)
            : new PrivateKeyFile(keyPath, passphrase);

        return [key];
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            await DisconnectAsync();
        }
        catch
        {
            // Disposal must not throw: the transport may already be gone.
        }

        _sftp?.Dispose();
        _ssh?.Dispose();
        _gate.Dispose();
    }
}
