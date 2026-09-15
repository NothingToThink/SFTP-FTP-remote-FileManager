namespace Core.Ssh.HostKey;

/// <summary>A single trusted host key, keyed by host and port.</summary>
public record KnownHostEntry(
    string Host,
    int Port,
    string Algorithm,
    string Sha256Fingerprint,
    DateTimeOffset TrustedAtUtc)
{
    public static string KeyFor(string host, int port) => $"{host.Trim().ToLowerInvariant()}:{port}";

    public string Key => KeyFor(Host, Port);
}
