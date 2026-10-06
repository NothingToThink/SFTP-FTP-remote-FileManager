using Backend.DTO;
using Core.Interfaces.Manager;
using Core.PortForwarding;
using Core.PortForwarding.Discovery;
using Core.Ssh;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

/// <summary>
/// Port forwarding for an existing SSH connection. Rules live as long as the connection does.
/// </summary>
[ApiController]
[Route("connections/{connectionId:guid}/forwards")]
public class PortForwardingController(
    ILogger<PortForwardingController> logger,
    IConnectionManager connectionManager,
    IPortForwardingManager portForwardingManager
) : ControllerBase
{
    [HttpGet]
    public IActionResult List(
        [FromRoute] Guid connectionId,
        [FromQuery] string? text,
        [FromQuery] ForwardType? type,
        [FromQuery] ForwardState? state,
        [FromQuery] int? portMin,
        [FromQuery] int? portMax)
    {
        var session = ResolveSession(connectionId);
        var filter = new ForwardFilter
        {
            Text = text,
            Type = type,
            State = state,
            PortMin = portMin,
            PortMax = portMax,
        };

        return Ok(portForwardingManager.List(session.Id, filter));
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromRoute] Guid connectionId,
        [FromBody] CreateForwardRequest request,
        CancellationToken ct)
    {
        var session = ResolveSession(connectionId);

        var rule = ForwardRule.Create(
            request.Name ?? string.Empty,
            request.Type,
            request.BindHost,
            request.BindPort,
            request.TargetHost,
            request.TargetPort);

        logger.LogInformation("Starting port forward {Rule} on connection {ConnectionId}.",
            rule.Describe(), connectionId);

        var status = await portForwardingManager.StartAsync(session, rule, ct);
        return Ok(status);
    }

    [HttpGet("{ruleId:guid}")]
    public IActionResult Get([FromRoute] Guid connectionId, [FromRoute] Guid ruleId)
    {
        var session = ResolveSession(connectionId);
        return Ok(portForwardingManager.Get(session.Id, ruleId));
    }

    [HttpPost("{ruleId:guid}/stop")]
    public async Task<IActionResult> Stop([FromRoute] Guid connectionId, [FromRoute] Guid ruleId, CancellationToken ct)
    {
        var session = ResolveSession(connectionId);
        logger.LogInformation("Stopping port forward {RuleId} on connection {ConnectionId}.", ruleId, connectionId);
        return Ok(await portForwardingManager.StopAsync(session.Id, ruleId, ct));
    }

    [HttpPost("{ruleId:guid}/start")]
    public async Task<IActionResult> Restart([FromRoute] Guid connectionId, [FromRoute] Guid ruleId, CancellationToken ct)
    {
        var session = ResolveSession(connectionId);
        var existing = portForwardingManager.Get(session.Id, ruleId);

        logger.LogInformation("Restarting port forward {Rule} on connection {ConnectionId}.",
            existing.Describe(), connectionId);

        return Ok(await portForwardingManager.StartAsync(session, existing.Rule, ct));
    }

    [HttpDelete("{ruleId:guid}")]
    public async Task<IActionResult> Delete([FromRoute] Guid connectionId, [FromRoute] Guid ruleId, CancellationToken ct)
    {
        var session = ResolveSession(connectionId);
        await portForwardingManager.RemoveAsync(session.Id, ruleId, ct);
        return NoContent();
    }

    /// <summary>
    /// Autocomplete for the forwarding target: ports actually listening on the server, merged with
    /// the well-known service catalog.
    /// </summary>
    [HttpGet("suggestions")]
    public async Task<IActionResult> Suggestions(
        [FromRoute] Guid connectionId,
        [FromQuery] string? text,
        [FromQuery] int? portMin,
        [FromQuery] int? portMax,
        [FromQuery] bool loopbackOnly,
        [FromQuery] int limit,
        CancellationToken ct)
    {
        var session = ResolveSession(connectionId);
        var filter = new SuggestionFilter
        {
            Text = text,
            PortMin = portMin,
            PortMax = portMax,
            LoopbackOnly = loopbackOnly,
            Limit = limit <= 0 ? 50 : limit,
        };

        return Ok(await portForwardingManager.SuggestTargetsAsync(session, filter, ct));
    }

    private ISshSession ResolveSession(Guid connectionId)
    {
        var connection = connectionManager.GetConnection(connectionId);

        if (connection is not ISshSessionProvider provider)
            throw new InvalidOperationException(
                "Port forwarding requires an SFTP/SSH connection; this connection uses a different protocol.");

        return provider.Session;
    }
}
