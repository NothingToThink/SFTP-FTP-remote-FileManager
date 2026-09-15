namespace Core.PortForwarding;

public enum ForwardType
{
    /// <summary>
    /// <c>ssh -L</c>: listen locally, tunnel to <c>TargetHost:TargetPort</c> as resolved from the server.
    /// </summary>
    Local,

    /// <summary>
    /// <c>ssh -R</c>: the server listens, traffic is tunnelled back and resolved from this machine.
    /// </summary>
    Remote,

    /// <summary>
    /// <c>ssh -D</c>: local SOCKS proxy; the destination is chosen per connection by the client.
    /// </summary>
    Dynamic,
}
