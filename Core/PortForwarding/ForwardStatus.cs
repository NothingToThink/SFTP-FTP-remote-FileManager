namespace Core.PortForwarding;

public enum ForwardState
{
    Stopped,
    Active,

    /// <summary>The tunnel was started but the SSH layer reported an error on it.</summary>
    Failed,
}

/// <summary>A rule plus its current runtime state.</summary>
public record ForwardStatus(
    ForwardRule Rule,
    ForwardState State,
    int? ActualBindPort,
    string? Error,
    DateTimeOffset? StartedAtUtc,
    long ConnectionsServed)
{
    public string Describe() => Rule.Describe();
}
