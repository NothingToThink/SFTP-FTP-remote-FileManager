using Backend.DTO;
using Core.Interfaces.Manager;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

/// <summary>
/// Provides remote file system management endpoints for active connections.
/// </summary>
[ApiController]
[Route("connections/{connectionId:guid}/filesystem")]
[Produces("application/json")]
[Tags("Connections Filesystem")]
public class ConnectionsFilesystemController(
  ILogger<ConnectionsFilesystemController> logger,
  IConnectionManager connectionManager
) : ControllerBase
{
    /// <summary>
    /// Gets all files and folders in the current working directory.
    /// </summary>
    /// <param name="connectionId">Unique connection identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A collection of items in the working directory.</returns>
    /// <response code="200">Files and directory list retrieved successfully.</response>
    /// <response code="404">Connection instance not found.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAllFiles([FromRoute] Guid connectionId, CancellationToken ct)
    {
        logger.LogInformation("Getting all files.");
        var connection = connectionManager.GetConnection(connectionId);
        var workingDir = await connection.GetWorkingDirectoryAsync(ct);
        return Ok(await connection.GetFilesAsync(workingDir, ct));
    }

    /// <summary>
    /// Retrieves detailed metadata for a specific file or folder.
    /// </summary>
    /// <param name="connectionId">Unique connection identifier.</param>
    /// <param name="path">Target path on the remote file system.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">File/folder metadata retrieved successfully.</response>
    /// <response code="404">Path or connection instance not found.</response>
    [HttpGet("info")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFileInfo([FromRoute] Guid connectionId, [FromBody] string path, CancellationToken ct)
    {
        logger.LogInformation("Getting file info.");
        var connection = connectionManager.GetConnection(connectionId);
        return Ok(await connection.GetInfoAsync(path, ct));
    }

    /// <summary>
    /// Calculates the total size of a directory, including all nested files.
    /// </summary>
    /// <param name="connectionId">Unique connection identifier.</param>
    /// <param name="path">Path of the directory on the remote file system.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Total size of the directory contents in bytes.</returns>
    /// <response code="200">Directory size calculated successfully.</response>
    /// <response code="404">Directory or connection instance not found.</response>
    [HttpGet("dir/size")]
    [ProducesResponseType(typeof(long), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDirSize([FromRoute] Guid connectionId, [FromBody] string path, CancellationToken ct)
    {
        logger.LogInformation("Getting directory size.");
        var connection = connectionManager.GetConnection(connectionId);
        return Ok(await connection.GetDirSizeAsync(path, ct));
    }

    /// <summary>
    /// Creates an empty file at the specified path.
    /// </summary>
    /// <param name="connectionId">Unique connection identifier.</param>
    /// <param name="path">Path where the file should be created.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">File created successfully.</response>
    /// <response code="400">Failed to create file due to invalid path or permissions.</response>
    [HttpPost("file")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateFile([FromRoute] Guid connectionId, [FromBody] string path, CancellationToken ct)
    {
        logger.LogInformation("Creating file.");
        var connection = connectionManager.GetConnection(connectionId);
        await connection.CreateFileAsync(path, ct);
        return Ok();
    }

    /// <summary>
    /// Creates a new directory at the specified path.
    /// </summary>
    /// <param name="connectionId">Unique connection identifier.</param>
    /// <param name="path">Path where the directory should be created.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Directory created successfully.</response>
    /// <response code="400">Failed to create directory.</response>
    [HttpPost("dir")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateDir([FromRoute] Guid connectionId, [FromBody] string path, CancellationToken ct)
    {
        logger.LogInformation("Creating directory.");
        var connection = connectionManager.GetConnection(connectionId);
        await connection.CreateDirAsync(path, ct);
        return Ok();
    }

    /// <summary>
    /// Deletes a file at the specified path.
    /// </summary>
    /// <param name="connectionId">Unique connection identifier.</param>
    /// <param name="path">Path of the file to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">File deleted successfully.</response>
    /// <response code="404">File not found.</response>
    [HttpDelete("file")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteFile([FromRoute] Guid connectionId, [FromBody] string path, CancellationToken ct)
    {
        logger.LogInformation("Deleting file.");
        var connection = connectionManager.GetConnection(connectionId);
        await connection.DeleteFileAsync(path, ct);
        return Ok();
    }

    /// <summary>
    /// Deletes a directory at the specified path.
    /// </summary>
    /// <param name="connectionId">Unique connection identifier.</param>
    /// <param name="path">Path of the directory to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Directory deleted successfully.</response>
    /// <response code="404">Directory not found.</response>
    [HttpDelete("dir")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteDir([FromRoute] Guid connectionId, [FromBody] string path, CancellationToken ct)
    {
        logger.LogInformation("Deleting directory.");
        var connection = connectionManager.GetConnection(connectionId);
        await connection.DeleteDirAsync(path, ct);
        return Ok();
    }

    /// <summary>
    /// Renames a file from an old path to a new path.
    /// </summary>
    /// <param name="connectionId">Unique connection identifier.</param>
    /// <param name="request">Request containing source path and target path.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">File renamed successfully.</response>
    /// <response code="400">Invalid parameters provided.</response>
    [HttpPatch("file")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RenameFile([FromRoute] Guid connectionId, [FromBody] RenameRequest request, CancellationToken ct)
    {
        logger.LogInformation("Renaming file.");
        var connection = connectionManager.GetConnection(connectionId);
        await connection.RenameFileAsync(request.oldPath, request.newPath, ct);
        return Ok();
    }

    /// <summary>
    /// Renames a directory from an old path to a new path.
    /// </summary>
    /// <param name="connectionId">Unique connection identifier.</param>
    /// <param name="request">Request containing source path and target path.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Directory renamed successfully.</response>
    /// <response code="400">Invalid parameters provided.</response>
    [HttpPatch("dir")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RenameDir([FromRoute] Guid connectionId, [FromBody] RenameRequest request, CancellationToken ct)
    {
        logger.LogInformation("Renaming directory.");
        var connection = connectionManager.GetConnection(connectionId);
        await connection.RenameDirAsync(request.oldPath, request.newPath, ct);
        return Ok();
    }

    /// <summary>
    /// Checks if a file exists at the specified path.
    /// </summary>
    /// <param name="connectionId">Unique connection identifier.</param>
    /// <param name="path">Target file path.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if the file exists; otherwise, <c>false</c>.</returns>
    /// <response code="200">Returns existence status.</response>
    [HttpGet("file/exists")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    public async Task<IActionResult> FileExists([FromRoute] Guid connectionId, [FromBody] string path, CancellationToken ct)
    {
        logger.LogInformation("Checking if the file exists.");
        var connection = connectionManager.GetConnection(connectionId);
        return Ok(await connection.FileExistsAsync(path, ct));
    }

    /// <summary>
    /// Checks if a directory exists at the specified path.
    /// </summary>
    /// <param name="connectionId">Unique connection identifier.</param>
    /// <param name="path">Target directory path.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if the directory exists; otherwise, <c>false</c>.</returns>
    /// <response code="200">Returns existence status.</response>
    [HttpGet("dir/exists")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    public async Task<IActionResult> DirExists([FromRoute] Guid connectionId, [FromBody] string path, CancellationToken ct)
    {
        logger.LogInformation("Checking if the directory exists.");
        var connection = connectionManager.GetConnection(connectionId);
        return Ok(await connection.DirExistsAsync(path, ct));
    }

    /// <summary>
    /// Copies a file to a new target path.
    /// </summary>
    /// <param name="connectionId">Unique connection identifier.</param>
    /// <param name="request">Copy parameters including source, target path, and override flag.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">File copied successfully.</response>
    /// <response code="400">Copy operation failed.</response>
    [HttpPost("file/copy")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CopyFile([FromRoute] Guid connectionId, [FromBody] CopyRequest request, CancellationToken ct)
    {
        logger.LogInformation("Copying file.");
        var connection = connectionManager.GetConnection(connectionId);
        await connection.CopyFileAsync(request.sourcePath, request.targetPath, request.canOverride, ct);
        return Ok();
    }

    /// <summary>
    /// Moves a file to a new target path.
    /// </summary>
    /// <param name="connectionId">Unique connection identifier.</param>
    /// <param name="request">Move parameters including source, target path, and override flag.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">File moved successfully.</response>
    /// <response code="400">Move operation failed.</response>
    [HttpPatch("file/move")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> MoveFile([FromRoute] Guid connectionId, [FromBody] MoveRequest request, CancellationToken ct)
    {
        logger.LogInformation("Moving file.");
        var connection = connectionManager.GetConnection(connectionId);
        await connection.MoveFileAsync(request.sourcePath, request.targetPath, request.canOverride, ct);
        return Ok();
    }

    /// <summary>
    /// Recursively copies a directory to a new target path.
    /// </summary>
    /// <param name="connectionId">Unique connection identifier.</param>
    /// <param name="request">Copy parameters including source, target path, and override flag.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Directory copied successfully.</response>
    /// <response code="400">Copy operation failed.</response>
    [HttpPost("dir/copy")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CopyDir([FromRoute] Guid connectionId, [FromBody] CopyRequest request, CancellationToken ct)
    {
        logger.LogInformation("Copying directory.");
        var connection = connectionManager.GetConnection(connectionId);
        await connection.CopyDirAsync(request.sourcePath, request.targetPath, request.canOverride, ct);
        return Ok();
    }

    /// <summary>
    /// Moves a directory to a new target path.
    /// </summary>
    /// <param name="connectionId">Unique connection identifier.</param>
    /// <param name="request">Move parameters including source, target path, and override flag.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Directory moved successfully.</response>
    /// <response code="400">Move operation failed.</response>
    [HttpPatch("dir/move")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> MoveDir([FromRoute] Guid connectionId, [FromBody] MoveRequest request, CancellationToken ct)
    {
        logger.LogInformation("Moving directory.");
        var connection = connectionManager.GetConnection(connectionId);
        await connection.MoveDirAsync(request.sourcePath, request.targetPath, request.canOverride, ct);
        return Ok();
    }

    /// <summary>
    /// Uploads a local file to the remote host file system.
    /// </summary>
    /// <param name="connectionId">Unique connection identifier.</param>
    /// <param name="remotePath">Target path on the remote host.</param>
    /// <param name="file">Form file binary content (<c>multipart/form-data</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">File uploaded successfully.</response>
    /// <response code="400">Invalid file or upload error.</response>
    [HttpPost("file/upload")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
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

    /// <summary>
    /// Downloads a file from the remote host file system.
    /// </summary>
    /// <param name="connectionId">Unique connection identifier.</param>
    /// <param name="path">Remote path of the file to download.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A binary file stream response.</returns>
    /// <response code="200">File stream returned successfully.</response>
    /// <response code="404">File not found.</response>
    [HttpGet("file/download")]
    [Produces("application/octet-stream")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(
        [FromRoute] Guid connectionId,
        [FromQuery] string path,
        CancellationToken ct)
    {
        var connection = connectionManager.GetConnection(connectionId);
        var stream = await connection.GetFileAsync(path, ct);
        return File(stream, "application/octet-stream", Path.GetFileName(path));
    }

    /// <summary>
    /// Retrieves the path of the current working directory.
    /// </summary>
    /// <param name="connectionId">Unique connection identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Absolute path of current working directory.</returns>
    /// <response code="200">Working directory path returned.</response>
    [HttpGet("dir/current")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCurrentDirectory([FromRoute] Guid connectionId, CancellationToken ct)
    {
        logger.LogInformation("Getting current directory.");
        var connection = connectionManager.GetConnection(connectionId);
        return Ok(await connection.GetWorkingDirectoryAsync(ct));
    }

    /// <summary>
    /// Changes the current working directory.
    /// </summary>
    /// <param name="connectionId">Unique connection identifier.</param>
    /// <param name="path">New working directory path.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Working directory changed successfully.</response>
    /// <response code="400">Target directory path is invalid or non-existent.</response>
    [HttpPatch("dir/current")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangeCurrentDirectory([FromRoute] Guid connectionId, [FromBody] string path, CancellationToken ct)
    {
        logger.LogInformation("Changing current directory.");
        var connection = connectionManager.GetConnection(connectionId);
        await connection.ChangeDirectoryAsync(path, ct);
        return Ok();
    }
}
