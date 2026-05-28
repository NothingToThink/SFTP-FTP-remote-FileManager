using Backend.DTO;
using Core.Interfaces.Manager;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

[ApiController]
[Route("connections/{connectionId}/filesystem")]
public class ConnectionsFilesystemController(
    ILogger<ConnectionsFilesystemController> logger,
    IConnectionManager connectionManager
) : ControllerBase
{
    [HttpGet]
    public IActionResult GetAllFiles([FromRoute] Guid connectionId)
    {
        logger.LogInformation("Getting all files.");
        var connection = connectionManager.GetConnection(connectionId);
        return Ok(connection.GetFiles(connection.GetWorkingDirectory()));
    }

    [HttpGet("info")]
    public IActionResult GetFileInfo([FromRoute] Guid connectionId, [FromBody] string path)
    {
        logger.LogInformation("Getting file info.");
        var connection = connectionManager.GetConnection(connectionId);
        return Ok(connection.GetInfo(path));
    }

    [HttpPost("file")]
    public IActionResult CreateFile([FromRoute] Guid connectionId, [FromBody] string path)
    {
        logger.LogInformation("Creating file.");
        var connection = connectionManager.GetConnection(connectionId);
        connection.CreateFile(path);
        return Ok();
    }

    [HttpPost("dir")]
    public IActionResult CreateDir([FromRoute] Guid connectionId, [FromBody] string path)
    {
        logger.LogInformation("Creating directory.");
        var connection = connectionManager.GetConnection(connectionId);
        connection.CreateDir(path);
        return Ok();
    }

    [HttpDelete("file")]
    public IActionResult DeleteFile([FromRoute] Guid connectionId, [FromBody] string path)
    {
        logger.LogInformation("Deleting file.");
        var connection = connectionManager.GetConnection(connectionId);
        connection.DeleteFile(path);
        return Ok();
    }

    [HttpDelete("dir")]
    public IActionResult DeleteDir([FromRoute] Guid connectionId, [FromBody] string path)
    {
        logger.LogInformation("Deleting directory.");
        var connection = connectionManager.GetConnection(connectionId);
        connection.DeleteDir(path);
        return Ok();
    }

    [HttpPatch("file")]
    public IActionResult RenameFile([FromRoute] Guid connectionId, [FromBody] RenameRequest request)
    {
        logger.LogInformation("Renaming file.");
        var connection = connectionManager.GetConnection(connectionId);
        connection.RenameFile(request.oldPath, request.newPath);
        return Ok();
    }

    [HttpPatch("dir")]
    public IActionResult RenameDir([FromRoute] Guid connectionId, [FromBody] RenameRequest request)
    {
        logger.LogInformation("Renaming directory.");
        var connection = connectionManager.GetConnection(connectionId);
        connection.RenameDir(request.oldPath, request.newPath);
        return Ok();
    }

    [HttpGet("file/exists")]
    public IActionResult FileExists([FromRoute] Guid connectionId, [FromBody] string path)
    {
        logger.LogInformation("Checking if the file exists.");
        var connection = connectionManager.GetConnection(connectionId);
        return Ok(connection.FileExists(path));
    }

    [HttpGet("dir/exists")]
    public IActionResult DirExists([FromRoute] Guid connectionId, [FromBody] string path)
    {
        logger.LogInformation("Checking if the directory exists.");
        var connection = connectionManager.GetConnection(connectionId);
        return Ok(connection.DirExists(path));
    }

    [HttpPost("file/copy")]
    public IActionResult CopyFile([FromRoute] Guid connectionId, [FromBody] CopyRequest request)
    {
        logger.LogInformation("Copying file.");
        var connection = connectionManager.GetConnection(connectionId);
        connection.CopyFile(request.sourcePath, request.targetPath, request.canOverride);
        return Ok();
    }

    [HttpPatch("file/move")]
    public IActionResult MoveFile([FromRoute] Guid connectionId, [FromBody] MoveRequest request)
    {
        logger.LogInformation("Moving file.");
        var connection = connectionManager.GetConnection(connectionId);
        connection.MoveFile(request.sourcePath, request.targetPath, request.canOverride);
        return Ok();
    }

    [HttpPost("file/upload")]
    public async Task<IActionResult> UploadFile(
        [FromRoute] Guid connectionId,
        [FromQuery] string remotePath,
        IFormFile file)
    {
        var connection = connectionManager.GetConnection(connectionId);
        await using var stream = file.OpenReadStream();
        connection.SaveFile(remotePath, stream);
        return Ok();
    }

    [HttpGet("file/download")]
    public IActionResult Download(
        [FromRoute] Guid connectionId,
        [FromQuery] string path)
    {
        var connection = connectionManager.GetConnection(connectionId);
        var stream = connection.GetFile(path);
        return File(stream, "application/octet-stream", Path.GetFileName(path));
    }

    [HttpGet("dir/current")]
    public IActionResult GetCurrentDirectory([FromRoute] Guid connectionId)
    {
        logger.LogInformation("Getting current directory.");
        var connection = connectionManager.GetConnection(connectionId);
        return Ok(connection.GetWorkingDirectory());
    }

    [HttpPatch("dir/current")]
    public IActionResult ChangeCurrentDirectory([FromRoute] Guid connectionId, [FromBody] string path)
    {
        logger.LogInformation("Changing current directory.");
        var connection = connectionManager.GetConnection(connectionId);
        connection.ChangeDirectory(path);
        return Ok();
    }
}