namespace Core.Ssh.HostKey;

public enum HostKeyVerdict
{
    /// <summary>No key is stored for this host yet.</summary>
    Unknown,

    /// <summary>The presented key matches the stored one.</summary>
    Trusted,

    /// <summary>A key is stored for this host but it is a different one. Possible MITM.</summary>
    Mismatch,
}
