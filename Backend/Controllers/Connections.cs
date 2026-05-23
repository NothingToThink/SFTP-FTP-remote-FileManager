using Backend.DTO;
using Core.Interfaces.Manager;
using Core.Models.Credentials;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Configuration;

namespace Backend.Controllers;

[ApiController]
[Route("connection")]
public class Connections(ILogger<Connections> logger, IConnectionManager connectionManager)
    : ControllerBase
{
    [HttpGet("/profiles")]
    public IActionResult GetAll()
    {
        logger.LogInformation("Getting all profiles");
        var profiles = connectionManager.GetProfilesList();
        return Ok(profiles);
    }

    [HttpGet("/connect/{id}")]
    public IActionResult ConnectById([FromBody] Guid id)
    {
        logger.LogInformation("Getting connection profile");
        try
        {
            var connection = connectionManager.GetConnection(id);
            connection.Connect();
            return Ok();
        } catch (Exception e)
        {
            logger.LogError(e, "Error while connecting to credentials");
            return BadRequest();
        }
    }
    
    [HttpPost("/connect")]
    public IActionResult ConnectByProfile([FromBody] ConnectRequest request)
    {
        logger.LogInformation("Connecting to credentials");
        var profile = SavedProfile.Create(request.Name, request.HostProfile);
        var id = connectionManager.CreateConnection(profile);
        return Ok(id);
    }

    [HttpPost("/disconnect")]
    public IActionResult DisconnectById([FromBody] Guid id)
    {
        logger.LogInformation("Deleting connection profile");
        try
        {
            connectionManager.DeleteConnection(id);
            return Ok();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while deleting connection");
            return BadRequest();
        }
    }

    [HttpPost("/test")]
    public IActionResult Test([FromBody] Guid id)
    {
        logger.LogInformation("Testing connection");
        try
        {
            var connection = connectionManager.GetConnection(id);
            return Ok(connection.IsConnected);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while testing connection");
            return BadRequest();
        }
    }
    
    
}