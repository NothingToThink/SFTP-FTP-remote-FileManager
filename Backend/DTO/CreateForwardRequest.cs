using Core.PortForwarding;

namespace Backend.DTO;

/// <param name="BindPort">0 lets the operating system choose a free port; the chosen one comes back in the response.</param>
public record CreateForwardRequest(
    ForwardType Type,
    int BindPort,
    string? Name = null,
    string? BindHost = null,
    string? TargetHost = null,
    int? TargetPort = null);
