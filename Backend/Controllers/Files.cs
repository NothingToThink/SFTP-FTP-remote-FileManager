using Core.Interfaces.Manager;
using Core.Interfaces.Protocol;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;



[ApiController]
[Route("files/{connectionId}")]
public class Files(ILogger<Connections> logger, IConnectionManager connectionManager) : ControllerBase
{
    private Connection GetConnection(Guid id)
    {
        var connection = connectionManager.GetConnection(id);
        return connection;
    }

    [HttpGet]
    public IActionResult GetFiles(Guid connectionId)
    {
        try
        {
            var connection = GetConnection(connectionId);
            var files = connection.GetFiles(connection.GetWorkingDirectory());
            return Ok(files);
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return BadRequest();
        }
    }
    
    
}