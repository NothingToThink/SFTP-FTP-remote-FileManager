namespace Core.Ssh;

/// <summary>
/// Implemented by connections that are backed by an SSH transport, so features layered on SSH
/// (port forwarding, remote command execution) can reach the session behind a connection id.
/// </summary>
public interface ISshSessionProvider
{
    ISshSession Session { get; }
}
