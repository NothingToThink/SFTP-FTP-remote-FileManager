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
    public async Task<IActionResult> GetAllFiles([FromRoute] Guid connectionId, CancellationToken ct)
    {
        try
        {
            logger.LogInformation("Getting all files.");
            var connection = connectionManager.GetConnection(connectionId);
            var workingDir = await connection.GetWorkingDirectoryAsync(ct);
            return Ok(await connection.GetFilesAsync(workingDir, ct));
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while getting all files.");
            return BadRequest();
        }
    }

    [HttpGet("info")]
    public async Task<IActionResult> GetFileInfo([FromRoute] Guid connectionId, [FromBody] string path, CancellationToken ct)
    {
        try
        {
            logger.LogInformation("Getting file info.");
            var connection = connectionManager.GetConnection(connectionId);
            return Ok(await connection.GetInfoAsync(path, ct));
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while getting file info.");
            return BadRequest();
        }
    }

    [HttpPost("file")]
    public async Task<IActionResult> CreateFile([FromRoute] Guid connectionId, [FromBody] string path, CancellationToken ct)
    {
        try
        {
            logger.LogInformation("Creating file.");
            var connection = connectionManager.GetConnection(connectionId);
            await connection.CreateFileAsync(path, ct);
            return Ok();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while creating file.");
            return BadRequest();
        }
    }

    [HttpPost("dir")]
    public async Task<IActionResult> CreateDir([FromRoute] Guid connectionId, [FromBody] string path, CancellationToken ct)
    {
        try
        {
            logger.LogInformation("Creating directory.");
            var connection = connectionManager.GetConnection(connectionId);
            await connection.CreateDirAsync(path, ct);
            return Ok();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while creating directory.");
            return BadRequest();
        }
    }

    [HttpDelete("file")]
    public async Task<IActionResult> DeleteFile([FromRoute] Guid connectionId, [FromBody] string path, CancellationToken ct)
    {
        try
        {
            logger.LogInformation("Deleting file.");
            var connection = connectionManager.GetConnection(connectionId);
            await connection.DeleteFileAsync(path, ct);
            return Ok();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while deleting file.");
            return BadRequest();
        }
    }

    [HttpDelete("dir")]
    public async Task<IActionResult> DeleteDir([FromRoute] Guid connectionId, [FromBody] string path, CancellationToken ct)
    {
        try
        {
            logger.LogInformation("Deleting directory.");
            var connection = connectionManager.GetConnection(connectionId);
            await connection.DeleteDirAsync(path, ct);
            return Ok();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while deleting directory.");
            return BadRequest();
        }
    }

    [HttpPatch("file")]
    public async Task<IActionResult> RenameFile([FromRoute] Guid connectionId, [FromBody] RenameRequest request, CancellationToken ct)
    {
        try
        {
            logger.LogInformation("Renaming file.");
            var connection = connectionManager.GetConnection(connectionId);
            await connection.RenameFileAsync(request.oldPath, request.newPath, ct);
            return Ok();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while renaming file.");
            return BadRequest();
        }
    }

    [HttpPatch("dir")]
    public async Task<IActionResult> RenameDir([FromRoute] Guid connectionId, [FromBody] RenameRequest request, CancellationToken ct)
    {
        try
        {
            logger.LogInformation("Renaming directory.");
            var connection = connectionManager.GetConnection(connectionId);
            await connection.RenameDirAsync(request.oldPath, request.newPath, ct);
            return Ok();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while renaming directory.");
            return BadRequest();
        }
    }

    [HttpGet("file/exists")]
    public async Task<IActionResult> FileExists([FromRoute] Guid connectionId, [FromBody] string path, CancellationToken ct)
    {
        try
        {
            logger.LogInformation("Checking if the file exists.");
            var connection = connectionManager.GetConnection(connectionId);
            return Ok(await connection.FileExistsAsync(path, ct));
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while checking if the file exists.");
            return BadRequest();
        }
    }

    [HttpGet("dir/exists")]
    public async Task<IActionResult> DirExists([FromRoute] Guid connectionId, [FromBody] string path, CancellationToken ct)
    {
        try
        {
            logger.LogInformation("Checking if the directory exists.");
            var connection = connectionManager.GetConnection(connectionId);
            return Ok(await connection.DirExistsAsync(path, ct));
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while checking if the directory exists.");
            return BadRequest();
        }
    }

    [HttpPost("file/copy")]
    public async Task<IActionResult> CopyFile([FromRoute] Guid connectionId, [FromBody] CopyRequest request, CancellationToken ct)
    {
        try
        {
            logger.LogInformation("Copying file.");
            var connection = connectionManager.GetConnection(connectionId);
            await connection.CopyFileAsync(request.sourcePath, request.targetPath, request.canOverride, ct);
            return Ok();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while copying file.");
            return BadRequest();
        }
    }

    [HttpPatch("file/move")]
    public async Task<IActionResult> MoveFile([FromRoute] Guid connectionId, [FromBody] MoveRequest request, CancellationToken ct)
    {
        try
        {
            logger.LogInformation("Moving file.");
            var connection = connectionManager.GetConnection(connectionId);
            await connection.MoveFileAsync(request.sourcePath, request.targetPath, request.canOverride, ct);
            return Ok();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while moving file.");
            return BadRequest();
        }
    }

    [HttpPost("file/upload")]
    public async Task<IActionResult> UploadFile(
        [FromRoute] Guid connectionId,
        [FromQuery] string remotePath,
        IFormFile file,
        CancellationToken ct)
    {
        try
        {
            var connection = connectionManager.GetConnection(connectionId);
            await using var stream = file.OpenReadStream();
            await connection.SaveFileAsync(remotePath, stream, ct);
            return Ok();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while uploading file.");
            return BadRequest();
        }
    }

    [HttpGet("file/download")]
    public async Task<IActionResult> Download(
        [FromRoute] Guid connectionId,
        [FromQuery] string path,
        CancellationToken ct)
    {
        try
        {
            var connection = connectionManager.GetConnection(connectionId);
            var stream = await connection.GetFileAsync(path, ct);
            return File(stream, "application/octet-stream", Path.GetFileName(path));
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while downloading file.");
            return BadRequest();
        }
    }
    
    [HttpGet("dir/current")]
    public async Task<IActionResult> GetCurrentDirectory([FromRoute] Guid connectionId, CancellationToken ct)
    {
        try
        {
            logger.LogInformation("Getting current directory.");
            var connection = connectionManager.GetConnection(connectionId);
            return Ok(await connection.GetWorkingDirectoryAsync(ct));
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while getting current directory");
            return BadRequest();
        }
    }

    [HttpPatch("dir/current")]
    public async Task<IActionResult> ChangeCurrentDirectory([FromRoute] Guid connectionId, [FromBody] string path, CancellationToken ct)
    {
        try
        {
            logger.LogInformation("Changing current directory.");
            var connection = connectionManager.GetConnection(connectionId);
            await connection.ChangeDirectoryAsync(path, ct);
            return Ok();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while changing current directory");
            return BadRequest();
        }
    }
}