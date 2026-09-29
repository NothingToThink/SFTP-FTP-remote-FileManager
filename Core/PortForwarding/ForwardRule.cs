namespace Core.PortForwarding;

/// <summary>
/// A port forwarding rule. <see cref="TargetHost"/>/<see cref="TargetPort"/> are unused for
/// <see cref="ForwardType.Dynamic"/>, where the destination is decided per connection by the
/// SOCKS client.
/// </summary>
public record ForwardRule(
    Guid Id,
    string Name,
    ForwardType Type,
    string BindHost,
    int BindPort,
    string? TargetHost,
    int? TargetPort)
{
    public const string DefaultBindHost = "127.0.0.1";

    public static ForwardRule Create(
        string name,
        ForwardType type,
        string? bindHost,
        int bindPort,
        string? targetHost,
        int? targetPort)
    {
        var rule = new ForwardRule(
            Guid.NewGuid(),
            string.IsNullOrWhiteSpace(name) ? BuildDefaultName(type, targetHost, targetPort, bindPort) : name.Trim(),
            type,
            string.IsNullOrWhiteSpace(bindHost) ? DefaultBindHost : bindHost.Trim(),
            bindPort,
            string.IsNullOrWhiteSpace(targetHost) ? null : targetHost.Trim(),
            targetPort);

        rule.Validate();
        return rule;
    }

    /// <summary>Human-readable form, mirroring the equivalent OpenSSH flag.</summary>
    public string Describe() => Type switch
    {
        ForwardType.Local => $"-L {BindHost}:{BindPort}:{TargetHost}:{TargetPort}",
        ForwardType.Remote => $"-R {BindHost}:{BindPort}:{TargetHost}:{TargetPort}",
        ForwardType.Dynamic => $"-D {BindHost}:{BindPort}",
        _ => $"{Type} {BindHost}:{BindPort}",
    };

    public void Validate()
    {
        // Port 0 is legal for binding (the OS picks a free port) but never as a destination.
        if (BindPort is < 0 or > 65535)
            throw new InvalidOperationException($"Bind port {BindPort} is out of range (0-65535).");

        if (string.IsNullOrWhiteSpace(BindHost))
            throw new InvalidOperationException("Bind host is required.");

        if (Type == ForwardType.Dynamic)
            return;

        if (string.IsNullOrWhiteSpace(TargetHost))
            throw new InvalidOperationException($"{Type} forwarding requires a target host.");

        if (TargetPort is null or < 1 or > 65535)
            throw new InvalidOperationException($"{Type} forwarding requires a target port in range 1-65535.");
    }

    private static string BuildDefaultName(ForwardType type, string? targetHost, int? targetPort, int bindPort)
        => type == ForwardType.Dynamic
            ? $"SOCKS :{bindPort}"
            : $"{targetHost}:{targetPort} via :{bindPort}";
}
