namespace Core.Ssh.HostKey;

/// <summary>
/// Raised when a server presents a host key that differs from the one previously trusted.
/// Treated as a hard failure: the connection is refused rather than silently re-trusted.
/// </summary>
public class HostKeyMismatchException : Exception
{
    public string Host { get; }
    public int Port { get; }
    public HostKeyInfo Presented { get; }
    public KnownHostEntry Expected { get; }

    public HostKeyMismatchException(string host, int port, HostKeyInfo presented, KnownHostEntry expected)
        : base($"Host key verification failed for {host}:{port}. " +
               $"Expected {expected.Algorithm} SHA256:{expected.Sha256Fingerprint}, " +
               $"got {presented.Algorithm} {presented.Display}. " +
               "If the server was legitimately rebuilt, forget the stored key and reconnect.")
    {
        Host = host;
        Port = port;
        Presented = presented;
        Expected = expected;
    }
}

/// <summary>
/// Raised under <see cref="HostKeyPolicy.Strict"/> when the host is not in the known-hosts store.
/// </summary>
public class UnknownHostKeyException : Exception
{
    public string Host { get; }
    public int Port { get; }
    public HostKeyInfo Presented { get; }

    public UnknownHostKeyException(string host, int port, HostKeyInfo presented)
        : base($"Host {host}:{port} is not known. Presented key: {presented.Algorithm} {presented.Display}. " +
               "Trust it explicitly before connecting under the strict host key policy.")
    {
        Host = host;
        Port = port;
        Presented = presented;
    }
}
