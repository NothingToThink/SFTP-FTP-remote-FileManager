namespace Core.PortForwarding.Discovery;

public enum SuggestionSource
{
    /// <summary>Observed listening on the remote host.</summary>
    RemoteScan,

    /// <summary>Taken from the well-known service catalog.</summary>
    Catalog,
}

/// <summary>An autocomplete candidate for the target of a forwarding rule.</summary>
/// <param name="Address">Listen address observed on the server, e.g. <c>127.0.0.1</c> or <c>0.0.0.0</c>.</param>
/// <param name="Service">Friendly service name, when known.</param>
/// <param name="Process">Process name reported by the server, when the user may read it.</param>
public record PortSuggestion(
    int Port,
    string? Address,
    string? Service,
    string? Process,
    SuggestionSource Source)
{
    /// <summary>
    /// A listener bound to loopback is the interesting case: it is exactly what is unreachable
    /// without a tunnel, so the UI can rank these first.
    /// </summary>
    public bool IsLoopbackOnly =>
        Address is "127.0.0.1" or "::1" or "localhost";

    public string Label
    {
        get
        {
            var name = Service ?? Process;
            return name is null ? Port.ToString() : $"{Port} — {name}";
        }
    }
}
