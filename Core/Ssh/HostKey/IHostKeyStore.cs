namespace Core.Ssh.HostKey;

public interface IHostKeyStore
{
    /// <summary>Compares a presented key against what is stored for the host.</summary>
    HostKeyVerdict Verify(string host, int port, HostKeyInfo presented, out KnownHostEntry? stored);

    /// <summary>Stores a key as trusted, replacing any previous entry for the host.</summary>
    void Trust(string host, int port, HostKeyInfo key);

    /// <summary>Removes the stored key for a host. Returns false if nothing was stored.</summary>
    bool Forget(string host, int port);

    IReadOnlyList<KnownHostEntry> List();
}
