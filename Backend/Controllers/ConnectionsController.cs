using Backend.DTO;
using Core.Interfaces.Manager;
using Core.Models.Credentials;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

[ApiController]
[Route("connections")]
public class ConnectionsController(
    ILogger<ConnectionsController> logger,
    IConnectionManager connectionManager
) : ControllerBase
{
    [HttpGet]
    public IActionResult GetConnectionIdList()
    {
        logger.LogInformation("Getting connection id list.");
        var connectionIdList = connectionManager.GetConnectionIdList();
        return Ok(connectionIdList);
    }

    [HttpPost]
    public IActionResult CreateConnection([FromBody] SavedProfile profile)
    {
        logger.LogInformation("Creating connection.");
        return Ok(connectionManager.CreateConnection(profile));
    }

    [HttpDelete("{connectionId}")]
    public IActionResult DeleteConnection([FromRoute] Guid connectionId)
    {
        logger.LogInformation("Deleting connection.");
        connectionManager.DeleteConnection(connectionId);
        return Ok();
    }

    [HttpPost("{connectionId}/connect")]
    public IActionResult ConnectConnecion([FromRoute] Guid connectionId)
    {
        logger.LogInformation("Connecting connection.");
        var connection = connectionManager.GetConnection(connectionId);
        connection.Connect();
        return Ok();
    }

    [HttpPost("{connectionId}/disconnect")]
    public IActionResult DisconnectConnection([FromRoute] Guid connectionId)
    {
        logger.LogInformation("Disconnecting connection.");
        var connection = connectionManager.GetConnection(connectionId);
        connection.Disconnect();
        return Ok();
    }

    [HttpGet("{connectionId}/state")]
    public IActionResult GetConnectionState([FromRoute] Guid connectionId)
    {
        logger.LogInformation("Getting connection state.");
        var connection = connectionManager.GetConnection(connectionId);
        return Ok(connection.IsConnected);
    }
}