using Backend.DTO;
using Core.Interfaces.Manager;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

/// <summary>
/// Provides remote file system operations for active connection instances.
/// </summary>
[ApiController]
[Route("connections/{connectionId}/filesystem")]
public class ConnectionsFilesystemController(
  ILogger<ConnectionsFilesystemController> logger,
  IConnectionManager connectionManager
) : ControllerBase
{
  /// <summary>
  /// Gets all files and directories in the current working directory.
  /// </summary>
  /// <param name="connectionId">Unique connection identifier.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>An <see cref="IActionResult"/> containing the collection of files and directories in the working directory.</returns>
  /// <response code="200">Returns the collection of files and directories.</response>
  [HttpGet]
  public async Task<IActionResult> GetAllFiles([FromRoute] Guid connectionId, CancellationToken ct)
  {
    logger.LogInformation("Getting all files.");
    var connection = connectionManager.GetConnection(connectionId);
    var workingDir = await connection.GetWorkingDirectoryAsync(ct);
    return Ok(await connection.GetFilesAsync(workingDir, ct));
  }

  /// <summary>
  /// Gets information for a file or directory at the specified path.
  /// </summary>
  /// <param name="connectionId">Unique connection identifier.</param>
  /// <param name="path">Target file system path.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>An <see cref="IActionResult"/> containing the file or directory information.</returns>
  /// <response code="200">Returns information about the specified file or directory.</response>
  [HttpGet("info")]
  public async Task<IActionResult> GetInfo([FromRoute] Guid connectionId, [FromBody] string path, CancellationToken ct)
  {
    logger.LogInformation("Getting file info.");
    var connection = connectionManager.GetConnection(connectionId);
    return Ok(await connection.GetInfoAsync(path, ct));
  }
  
  /// <summary>
  /// Gets the size of a directory at the specified path.
  /// </summary>
  /// <param name="connectionId">Unique connection identifier.</param>
  /// <param name="path">Target directory path.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>An <see cref="IActionResult"/> containing the size of the specified directory.</returns>
  /// <response code="200">Returns the size of the directory.</response>
  [HttpGet("dir/size")]
  public async Task<IActionResult> GetDirSize([FromRoute] Guid connectionId, [FromBody] string path, CancellationToken ct)
  {
    logger.LogInformation("Getting direcotory size.");
    var connection = connectionManager.GetConnection(connectionId);
    return Ok(await connection.GetDirSizeAsync(path, ct));
  }

    /// <summary>
    /// Creates a file at the specified path.
    /// </summary>
    /// <param name="connectionId">Unique connection identifier.</param>
    /// <param name="path">Target path for the new file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An <see cref="IActionResult"/> indicating the operation result.</returns>
    /// <response code="200">The file was successfully created.</response>
  [HttpPost("file")]
  public async Task<IActionResult> CreateFile([FromRoute] Guid connectionId, [FromBody] string path, CancellationToken ct)
  {
    logger.LogInformation("Creating file.");
    var connection = connectionManager.GetConnection(connectionId);
    await connection.CreateFileAsync(path, ct);
    return Ok();
  }

  /// <summary>
  /// Creates a directory at the specified path.
  /// </summary>
  /// <param name="connectionId">Unique connection identifier.</param>
  /// <param name="path">Target path for the new directory.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>An <see cref="IActionResult"/> indicating the operation result.</returns>
  /// <response code="200">The directory was successfully created.</response>
  [HttpPost("dir")]
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
  /// <returns>An <see cref="IActionResult"/> indicating the operation result.</returns>
  /// <response code="200">The file was successfully deleted.</response>
  [HttpDelete("file")]
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
  /// <returns>An <see cref="IActionResult"/> indicating the operation result.</returns>
  /// <response code="200">The directory was successfully deleted.</response>
  [HttpDelete("dir")]
  public async Task<IActionResult> DeleteDir([FromRoute] Guid connectionId, [FromBody] string path, CancellationToken ct)
  {
    logger.LogInformation("Deleting directory.");
    var connection = connectionManager.GetConnection(connectionId);
    await connection.DeleteDirAsync(path, ct);
    return Ok();
  }

  /// <summary>
  /// Renames a file.
  /// </summary>
  /// <param name="connectionId">Unique connection identifier.</param>
  /// <param name="request">Request containing the old and new file paths.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>An <see cref="IActionResult"/> indicating the operation result.</returns>
  /// <response code="200">The file was successfully renamed.</response>
  [HttpPatch("file")]
  public async Task<IActionResult> RenameFile([FromRoute] Guid connectionId, [FromBody] RenameRequest request, CancellationToken ct)
  {
    logger.LogInformation("Renaming file.");
    var connection = connectionManager.GetConnection(connectionId);
    await connection.RenameFileAsync(request.oldPath, request.newPath, ct);
    return Ok();
  }

  /// <summary>
  /// Renames a directory.
  /// </summary>
  /// <param name="connectionId">Unique connection identifier.</param>
  /// <param name="request">Request containing the old and new directory paths.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>An <see cref="IActionResult"/> indicating the operation result.</returns>
  /// <response code="200">The directory was successfully renamed.</response>
  [HttpPatch("dir")]
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
  /// <returns>An <see cref="IActionResult"/> containing a boolean indicating whether the file exists.</returns>
  /// <response code="200">Returns true if the file exists; otherwise, false.</response>
  [HttpGet("file/exists")]
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
  /// <returns>An <see cref="IActionResult"/> containing a boolean indicating whether the directory exists.</returns>
  /// <response code="200">Returns true if the directory exists; otherwise, false.</response>
  [HttpGet("dir/exists")]
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
  /// <param name="request">Request parameters for copying the file.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>An <see cref="IActionResult"/> indicating the operation result.</returns>
  /// <response code="200">The file was successfully copied.</response>
  [HttpPost("file/copy")]
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
  /// <param name="request">Request parameters for moving the file.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>An <see cref="IActionResult"/> indicating the operation result.</returns>
  /// <response code="200">The file was successfully moved.</response>
  [HttpPatch("file/move")]
  public async Task<IActionResult> MoveFile([FromRoute] Guid connectionId, [FromBody] MoveRequest request, CancellationToken ct)
  {
    logger.LogInformation("Moving file.");
    var connection = connectionManager.GetConnection(connectionId);
    await connection.MoveFileAsync(request.sourcePath, request.targetPath, request.canOverride, ct);
    return Ok();
  }

  /// <summary>
  /// Copies a directory to a new target path.
  /// </summary>
  /// <param name="connectionId">Unique connection identifier.</param>
  /// <param name="request">Request parameters for copying the directory.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>An <see cref="IActionResult"/> indicating the operation result.</returns>
  /// <response code="200">The directory was successfully copied.</response>
  [HttpPost("dir/copy")]
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
  /// <param name="request">Request parameters for moving the directory.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>An <see cref="IActionResult"/> indicating the operation result.</returns>
  /// <response code="200">The directory was successfully moved.</response>
  [HttpPatch("dir/move")]
  public async Task<IActionResult> MoveDir([FromRoute] Guid connectionId, [FromBody] MoveRequest request, CancellationToken ct)
  {
    logger.LogInformation("Moving directory.");
    var connection = connectionManager.GetConnection(connectionId);
    await connection.MoveDirAsync(request.sourcePath, request.targetPath, request.canOverride, ct);
    return Ok();
  }

  /// <summary>
  /// Uploads a file to the remote path.
  /// </summary>
  /// <param name="connectionId">Unique connection identifier.</param>
  /// <param name="remotePath">Target destination path on the remote host.</param>
  /// <param name="file">File payload provided via form data.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>An <see cref="IActionResult"/> indicating the operation result.</returns>
  /// <response code="200">The file was successfully uploaded.</response>
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

  /// <summary>
  /// Downloads a file from the remote path.
  /// </summary>
  /// <param name="connectionId">Unique connection identifier.</param>
  /// <param name="path">Remote path of the file to download.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>A file stream result containing the requested file content.</returns>
  /// <response code="200">Returns the requested file stream for download.</response>
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

  /// <summary>
  /// Retrieves the current working directory path.
  /// </summary>
  /// <param name="connectionId">Unique connection identifier.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>An <see cref="IActionResult"/> containing the current working directory path.</returns>
  /// <response code="200">Returns the current working directory path.</response>
  [HttpGet("dir/current")]
  public async Task<IActionResult> GetCurrentDirectory([FromRoute] Guid connectionId, CancellationToken ct)
  {
    logger.LogInformation("Getting current directory.");
    var connection = connectionManager.GetConnection(connectionId);
    return Ok(await connection.GetWorkingDirectoryAsync(ct));
  }

  /// <summary>
  /// Changes the current working directory path.
  /// </summary>
  /// <param name="connectionId">Unique connection identifier.</param>
  /// <param name="path">New working directory path.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>An <see cref="IActionResult"/> indicating the operation result.</returns>
  /// <response code="200">The working directory was successfully changed.</response>
  [HttpPatch("dir/current")]
  public async Task<IActionResult> ChangeCurrentDirectory([FromRoute] Guid connectionId, [FromBody] string path, CancellationToken ct)
  {
    logger.LogInformation("Changing current directory.");
    var connection = connectionManager.GetConnection(connectionId);
    await connection.ChangeDirectoryAsync(path, ct);
    return Ok();
  }
}
