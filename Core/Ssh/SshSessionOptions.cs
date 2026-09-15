namespace Core.Ssh;

public enum HostKeyPolicy
{
    /// <summary>Trust on first use: an unknown key is recorded, a changed key is refused.</summary>
    TrustOnFirstUse,

    /// <summary>Only keys already present in the store are accepted.</summary>
    Strict,
}

public class SshSessionOptions
{
    public HostKeyPolicy HostKeyPolicy { get; set; } = HostKeyPolicy.TrustOnFirstUse;

    /// <summary>How long the SSH transport may sit unused before the janitor may close it.</summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Keep-alive interval, so NAT and idle-timeout middleboxes do not silently drop tunnels.</summary>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(20);
}
