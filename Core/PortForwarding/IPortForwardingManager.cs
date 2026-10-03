using Core.PortForwarding.Discovery;
using Core.Ssh;

namespace Core.PortForwarding;

public interface IPortForwardingManager
{
    /// <summary>Creates a rule and starts the tunnel. The rule is not stored if the start fails.</summary>
    Task<ForwardStatus> StartAsync(ISshSession session, ForwardRule rule, CancellationToken ct = default);

    Task<ForwardStatus> StopAsync(Guid sessionId, Guid ruleId, CancellationToken ct = default);

    /// <summary>Stops the tunnel and forgets the rule.</summary>
    Task RemoveAsync(Guid sessionId, Guid ruleId, CancellationToken ct = default);

    ForwardStatus Get(Guid sessionId, Guid ruleId);

    /// <summary>Rules for one session, narrowed by <paramref name="filter"/>.</summary>
    IReadOnlyList<ForwardStatus> List(Guid sessionId, ForwardFilter? filter = null);

    /// <summary>Autocomplete candidates for a forwarding target: remote scan plus service catalog.</summary>
    Task<IReadOnlyList<PortSuggestion>> SuggestTargetsAsync(
        ISshSession session,
        SuggestionFilter? filter = null,
        CancellationToken ct = default);

    /// <summary>Stops and forgets every rule of a session. Called when its connection goes away.</summary>
    Task RemoveSessionAsync(Guid sessionId);
}
