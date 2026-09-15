using Core.Ssh;

namespace Core.PortForwarding.Discovery;

public interface IRemotePortScanner
{
    /// <summary>
    /// Lists TCP ports currently listening on the remote host, for autocompleting forwarding targets.
    /// Returns an empty list if the server exposes no usable way to enumerate them.
    /// </summary>
    Task<IReadOnlyList<PortSuggestion>> ScanAsync(ISshSession session, CancellationToken ct = default);
}
