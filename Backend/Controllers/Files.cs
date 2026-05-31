using Core.Interfaces.Manager;
using Core.Interfaces.Protocol;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

[ApiController]
[Route("files/{connectionId}")]
public class Files(ILogger<Files> logger, IConnectionManager connectionManager) : ControllerBase
{
    private Connection GetConnection(Guid id)
    {
        var connection = connectionManager.GetConnection(id);
        return connection;
    }

    [HttpGet]
    public async Task<IActionResult> GetFiles([FromRoute] Guid connectionId, CancellationToken ct)
    {
        try
        {
            var connection = GetConnection(connectionId);
            var workingDir = await connection.GetWorkingDirectoryAsync(ct);
            var files = await connection.GetFilesAsync(workingDir, ct);
            return Ok(files);
        }
        catch (Exception e)
        {
            logger.LogError(e, e.Message);
            return BadRequest();
        }
    }
}