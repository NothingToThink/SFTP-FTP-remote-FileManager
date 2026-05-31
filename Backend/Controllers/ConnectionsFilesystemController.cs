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
            logger.LogInformation("Getting all files.");
            var connection = connectionManager.GetConnection(connectionId);
            var workingDir = await connection.GetWorkingDirectoryAsync(ct);
            return Ok(await connection.GetFilesAsync(workingDir, ct));
    }

    [HttpGet("info")]
    public async Task<IActionResult> GetFileInfo([FromRoute] Guid connectionId, [FromBody] string path, CancellationToken ct)
    {
            logger.LogInformation("Getting file info.");
            var connection = connectionManager.GetConnection(connectionId);
            return Ok(await connection.GetInfoAsync(path, ct));
        
    }

    [HttpPost("file")]
    public async Task<IActionResult> CreateFile([FromRoute] Guid connectionId, [FromBody] string path, CancellationToken ct)
    {
            logger.LogInformation("Creating file.");
            var connection = connectionManager.GetConnection(connectionId);
            await connection.CreateFileAsync(path, ct);
            return Ok();
    }

    [HttpPost("dir")]
    public async Task<IActionResult> CreateDir([FromRoute] Guid connectionId, [FromBody] string path, CancellationToken ct)
    {
            logger.LogInformation("Creating directory.");
            var connection = connectionManager.GetConnection(connectionId);
            await connection.CreateDirAsync(path, ct);
            return Ok();
    }

    [HttpDelete("file")]
    public async Task<IActionResult> DeleteFile([FromRoute] Guid connectionId, [FromBody] string path, CancellationToken ct)
    {
            logger.LogInformation("Deleting file.");
            var connection = connectionManager.GetConnection(connectionId);
            await connection.DeleteFileAsync(path, ct);
            return Ok();
    }

    [HttpDelete("dir")]
    public async Task<IActionResult> DeleteDir([FromRoute] Guid connectionId, [FromBody] string path, CancellationToken ct)
    {
            logger.LogInformation("Deleting directory.");
            var connection = connectionManager.GetConnection(connectionId);
            await connection.DeleteDirAsync(path, ct);
            return Ok();
    }

    [HttpPatch("file")]
    public async Task<IActionResult> RenameFile([FromRoute] Guid connectionId, [FromBody] RenameRequest request, CancellationToken ct)
    {
            logger.LogInformation("Renaming file.");
            var connection = connectionManager.GetConnection(connectionId);
            await connection.RenameFileAsync(request.oldPath, request.newPath, ct);
            return Ok();
    }

    [HttpPatch("dir")]
    public async Task<IActionResult> RenameDir([FromRoute] Guid connectionId, [FromBody] RenameRequest request, CancellationToken ct)
    {
            logger.LogInformation("Renaming directory.");
            var connection = connectionManager.GetConnection(connectionId);
            await connection.RenameDirAsync(request.oldPath, request.newPath, ct);
            return Ok();
    }

    [HttpGet("file/exists")]
    public async Task<IActionResult> FileExists([FromRoute] Guid connectionId, [FromBody] string path, CancellationToken ct)
    {
            logger.LogInformation("Checking if the file exists.");
            var connection = connectionManager.GetConnection(connectionId);
            return Ok(await connection.FileExistsAsync(path, ct));
    }

    [HttpGet("dir/exists")]
    public async Task<IActionResult> DirExists([FromRoute] Guid connectionId, [FromBody] string path, CancellationToken ct)
    {
            logger.LogInformation("Checking if the directory exists.");
            var connection = connectionManager.GetConnection(connectionId);
            return Ok(await connection.DirExistsAsync(path, ct));
        
    }

    [HttpPost("file/copy")]
    public async Task<IActionResult> CopyFile([FromRoute] Guid connectionId, [FromBody] CopyRequest request, CancellationToken ct)
    {
            logger.LogInformation("Copying file.");
            var connection = connectionManager.GetConnection(connectionId);
            await connection.CopyFileAsync(request.sourcePath, request.targetPath, request.canOverride, ct);
            return Ok();
    }

    [HttpPatch("file/move")]
    public async Task<IActionResult> MoveFile([FromRoute] Guid connectionId, [FromBody] MoveRequest request, CancellationToken ct)
    {
            logger.LogInformation("Moving file.");
            var connection = connectionManager.GetConnection(connectionId);
            await connection.MoveFileAsync(request.sourcePath, request.targetPath, request.canOverride, ct);
            return Ok();
    }

    [HttpPost("file/upload")]
    public async Task<IActionResult> UploadFile(
        [FromRoute] Guid connectionId,
        [FromQuery] string remotePath,
        IFormFile file,
        CancellationToken ct)
    {
            var connection = connectionManager.GetConnection(connectionId);
            await using var stream = file.OpenReadStream();
            await connection.SaveFileAsync(remotePath, stream, ct);
            return Ok();
    }

    [HttpGet("file/download")]
    public async Task<IActionResult> Download(
        [FromRoute] Guid connectionId,
        [FromQuery] string path,
        CancellationToken ct)
    {
            var connection = connectionManager.GetConnection(connectionId);
            var stream = await connection.GetFileAsync(path, ct);
            return File(stream, "application/octet-stream", Path.GetFileName(path));
    }
    
    [HttpGet("dir/current")]
    public async Task<IActionResult> GetCurrentDirectory([FromRoute] Guid connectionId, CancellationToken ct)
    {
            logger.LogInformation("Getting current directory.");
            var connection = connectionManager.GetConnection(connectionId);
            return Ok(await connection.GetWorkingDirectoryAsync(ct));
    }

    [HttpPatch("dir/current")]
    public async Task<IActionResult> ChangeCurrentDirectory([FromRoute] Guid connectionId, [FromBody] string path, CancellationToken ct)
    {
            logger.LogInformation("Changing current directory.");
            var connection = connectionManager.GetConnection(connectionId);
            await connection.ChangeDirectoryAsync(path, ct);
            return Ok();
    }
}