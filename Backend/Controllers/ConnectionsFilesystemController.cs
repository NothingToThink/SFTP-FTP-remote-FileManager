using Backend.DTO;
using Core.Interfaces.Manager;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("connecion/{connectionId}/filesystem")]
public class ConnectionsFilesystemController(
        ILogger<ConnectionsFilesystemController> logger,
        IConnectionManager connectionManager
    ) : ControllerBase
{
    [HttpGet]
    public IActionResult GetAllFiles([FromRoute] Guid connectionId)
    {
        try
        {
            logger.LogInformation("Getting all files.");
            var connection = connectionManager.GetConnection(connectionId);
            return Ok(connection.GetFiles(connection.GetWorkingDirectory()));
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while getting all files.");
            return BadRequest();
        }
    }

    [HttpGet("info")]
    public IActionResult GetFileInfo([FromRoute] Guid connectionId, [FromBody] string path)
    {
        try
        {
            logger.LogInformation("Getting file info.");
            var connection = connectionManager.GetConnection(connectionId);
            return Ok(connection.GetInfo(path));
        } catch (Exception e)
        {
            logger.LogError(e, "Error while getting file info.");
            return BadRequest();
        }
    }

    [HttpPost("file")]
    public IActionResult CreateFile([FromRoute] Guid connectionId, [FromBody] string path)
    {
        try
        {
            logger.LogInformation("Creating file.");
            var connection = connectionManager.GetConnection(connectionId);
            connection.CreateFile(path);
            return Ok();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while creating file.");
            return BadRequest();
        }
    }

    [HttpPost("dir")]
    public IActionResult CreateDir([FromRoute] Guid connectionId, [FromBody] string path)
    {
        try
        {
            logger.LogInformation("Creating directory.");
            var connection = connectionManager.GetConnection(connectionId);
            connection.CreateFile(path);
            return Ok();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while creating directory.");
            return BadRequest();
        }
    }

    [HttpDelete("file")]
    public IActionResult DeleteFile([FromRoute] Guid connectionId, [FromBody] string path)
    {
        try
        {
            logger.LogInformation("Deleting file.");
            var connection = connectionManager.GetConnection(connectionId);
            connection.CreateFile(path);
            return Ok();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while deleting file.");
            return BadRequest();
        }
    }

    [HttpDelete("dir")]
    public IActionResult DeleteDir([FromRoute] Guid connectionId, [FromBody] string path)
    {
        try
        {
            logger.LogInformation("Deleting directory.");
            var connection = connectionManager.GetConnection(connectionId);
            connection.CreateFile(path);
            return Ok();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while deleting directory.");
            return BadRequest();
        }
    }

    [HttpPatch("file")]
    public IActionResult RenameFile([FromRoute] Guid connectionId, [FromBody] RenameRequest request)
    {
        try
        {
            logger.LogInformation("Renaming file.");
            var connection = connectionManager.GetConnection(connectionId);
            connection.RenameFile(request.oldPath, request.newPath);
            return Ok();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while renaming file.");
            return BadRequest();
        }
    }

    [HttpPatch("dir")]
    public IActionResult RenameDir([FromRoute] Guid connectionId, [FromBody] RenameRequest request)
    {
        try
        {
            logger.LogInformation("Renaming directory.");
            var connection = connectionManager.GetConnection(connectionId);
            connection.RenameFile(request.oldPath, request.newPath);
            return Ok();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while renaming directory.");
            return BadRequest();
        }
    }

    [HttpGet("file/exists")]
    public IActionResult FileExists([FromRoute] Guid connectionId, [FromBody] string path)
    {
        try
        {
            logger.LogInformation("Checking if the file exists.");
            var connection = connectionManager.GetConnection(connectionId);
            return Ok(connection.FileExists(path));
        } catch (Exception e)
        {
            logger.LogError(e, "Error while checking if the file exists.");
            return BadRequest();
        }
    }

    [HttpGet("dir/exists")]
    public IActionResult DirExists([FromRoute] Guid connectionId, [FromBody] string path)
    {
        try
        {
            logger.LogInformation("Checking if the directory exists.");
            var connection = connectionManager.GetConnection(connectionId);
            return Ok(connection.FileExists(path));
        } catch (Exception e)
        {
            logger.LogError(e, "Error while checking if the directory exists.");
            return BadRequest();
        }
    }

    [HttpPost("file/copy")]
    public IActionResult CopyFile([FromRoute] Guid connectionId, [FromBody] CopyRequest request)
    {
        throw new NotImplementedException();
        // try
        // {
        //     logger.LogInformation("Renaming file.");
        //     var connection = connectionManager.GetConnection(connectionId);
        //     connection.CopyFile(request.sourcePath, request.targetPath);
        //     return Ok();
        // }
        // catch (Exception e)
        // {
        //     logger.LogError(e, "Error while renaming file.");
        //     return BadRequest();
        // }
    }

    [HttpPatch("file/move")]
    public IActionResult MoveFile([FromRoute] Guid connectionId, [FromBody] MoveRequest request)
    {
        throw new NotImplementedException();
        // try
        // {
        //     logger.LogInformation("Renaming file.");
        //     var connection = connectionManager.GetConnection(connectionId);
        //     connection.CopyFile(request.sourcePath, request.targetPath);
        //     return Ok();
        // }
        // catch (Exception e)
        // {
        //     logger.LogError(e, "Error while renaming file.");
        //     return BadRequest();
        // }
    }

    [HttpPost("file/upload")]
    public IActionResult UploadFile([FromRoute] Guid connectionId, [FromBody] UploadRequest request)
    {
        logger.LogInformation($"Uploading file.");
        try
        {
            var connection = connectionManager.GetConnection(connectionId);
            byte[] bytes = Convert.FromBase64String(request.base64String);
            connection.SaveFile(request.remotePath, new MemoryStream(bytes));
            return Ok();
        }
        catch (Exception e)
        {
            logger.LogError(e, $"Error while uploading file.");
            return BadRequest();
        }
    }

    [HttpPost("file/download")]
    public IActionResult Download([FromRoute] Guid connectionId, [FromBody] string path)
    {
        logger.LogInformation($"Downloading file.");
        try
        {
            var connection = connectionManager.GetConnection(connectionId);
            var stream = connection.GetFile(path);
            byte[] bytes = new byte[stream.Length];
            stream.ReadExactly(bytes, 0, bytes.Count());
            return Ok(Convert.ToBase64String(bytes));
        }
        catch (Exception e)
        {
            logger.LogError(e, $"Error while downloading file.");
            return BadRequest();
        }
    }

    [HttpGet("dir/current")]
    public IActionResult GetCurrentDirectory([FromRoute] Guid connectionId)
    {
        try
        {
            logger.LogInformation("Getting current directory.");
            var connection = connectionManager.GetConnection(connectionId);
            return Ok(connection.GetWorkingDirectory());
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while getting current directory");
            return BadRequest();
        }
    }

    [HttpPatch("dir/current")]
    public IActionResult ChangeCurrentDirectory([FromRoute] Guid connectionId, [FromBody] string path)
    {
        try
        {
            logger.LogInformation("Changing current directory.");
            var connection = connectionManager.GetConnection(connectionId);
            connection.ChangeDirectory(path);
            return Ok();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while changing current directory");
            return BadRequest();
        }
    }
}