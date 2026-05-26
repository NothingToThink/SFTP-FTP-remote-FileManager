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
        try
        {
            logger.LogInformation("Creating connection.");
            return Ok(connectionManager.CreateConnection(profile));
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while creating connection.");
            return BadRequest();
        }
    }

    [HttpDelete("{connectionId}")]
    public IActionResult DeleteConnection([FromRoute] Guid connectionId)
    {
        try
        {
            logger.LogInformation("Deleting connection.");
            connectionManager.DeleteConnection(connectionId);
            return Ok();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while deleting connection.");
            return BadRequest();
        }
    }

    [HttpPost("{connectionId}/connect")]
    public IActionResult ConnectConnecion([FromRoute] Guid connectionId)
    {
        try
        {
            logger.LogInformation("Connecting connection.");
            var connection = connectionManager.GetConnection(connectionId);
            connection.Connect();
            return Ok();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while connecting connection.");
            return BadRequest();
        }
    }

    [HttpPost("{connectionId}/disconnect")]
    public IActionResult DisconnectConnection([FromRoute] Guid connectionId)
    {
        try
        {
            logger.LogInformation("Disconnecting connection.");
            var connection = connectionManager.GetConnection(connectionId);
            connection.Disconnect();
            return Ok();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while disconnecting connection.");
            return BadRequest();
        }
    }

    [HttpGet("{connectionId}/state")]
    public IActionResult GetConnectionState([FromRoute] Guid connectionId)
    {
        try
        {
            logger.LogInformation("Getting connection state.");
            var connection = connectionManager.GetConnection(connectionId);
            return Ok(connection.IsConnected);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while getting connection state.");
            return BadRequest();
        }
    }
}
