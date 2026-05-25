// using Core.Interfaces.Manager;
// using Core.Interfaces.Protocol;
// using Microsoft.AspNetCore.Mvc;

// namespace Backend.Controllers;



// [ApiController]
// [Route("files/{connectionId}")]
// public class Files(ILogger<Files> logger, IConnectionManager connectionManager) : ControllerBase
// {
//     private Connection GetConnection(Guid id)
//     {
//         var connection = connectionManager.GetConnection(id);
//         return connection;
//     }

//     [HttpGet]
//     public IActionResult GetFiles(Guid connectionId)
//     {
//         try
//         {
//             var connection = GetConnection(connectionId);
//             var files = connection.GetFiles(connection.GetWorkingDirectory());
//             return Ok(files);
//         }
//         catch (Exception e)
//         {
//             logger.LogError(e, e.Message);
//             return BadRequest();
//         }
//     }

//     [HttpGet("info")]
//     public IActionResult GetFileInfo(Guid connectionId, [FromQuery] string path)
//     {
//         throw new NotImplementedException();
//     }

//     [HttpGet("exists")]
//     public IActionResult Exists(Guid connectionId, [FromQuery] string path)
//     {
//         throw new NotImplementedException();
//     }

//     [HttpPost("create-file")]
//     public IActionResult CreateFile(Guid connectionId, [FromBody] string path)
//     {
//         logger.LogInformation($"Creating file (connectionId {connectionId})");
//         try
//         {
//             var connection = GetConnection(connectionId);
//             connection.CreateFile(path);
//             return Ok();
//         }
//         catch (Exception e)
//         {
//             logger.LogError(e, $"Error while creating file (connectionId {connectionId})");
//             return BadRequest();
//         }
//     }

//     [HttpPost("create-dir")]
//     public IActionResult CreateDir(Guid connectionId, [FromBody] string path)
//     {
//         logger.LogInformation($"Creating directory (connectionId {connectionId})");
//         try
//         {
//             var connection = GetConnection(connectionId);
//             connection.CreateDir(path);
//             return Ok();
//         }
//         catch (Exception e)
//         {
//             logger.LogError(e, $"Error while creating directory (connectionId {connectionId})");
//             return BadRequest();
//         }
//     }

//     [HttpPost("delete-file")]
//     public IActionResult DeleteFile(Guid connectionId, [FromBody] string path)
//     {
//         logger.LogInformation($"Deleting file (connectionId {connectionId})");
//         try
//         {
//             var connection = GetConnection(connectionId);
//             connection.DeleteFile(path);
//             return Ok();
//         }
//         catch (Exception e)
//         {
//             logger.LogError(e, $"Error while deleting file (connectionId {connectionId})");
//             return BadRequest();
//         }
//     }

//     [HttpPost("delete-dir")]
//     public IActionResult DeleteDir(Guid connectionId, [FromBody] string path)
//     {
//         logger.LogInformation($"Deleting directory (connectionId {connectionId})");
//         try
//         {
//             var connection = GetConnection(connectionId);
//             connection.DeleteDir(path);
//             return Ok();
//         }
//         catch (Exception e)
//         {
//             logger.LogError(e, $"Error while deleting directory (connectionId {connectionId})");
//             return BadRequest();
//         }
//     }

//     [HttpPost("delete")]
//     public IActionResult Delete(Guid connectionId, List<string> paths)
//     {
//         logger.LogInformation($"Deleting paths (connectionId {connectionId})");
//         try
//         {
//             var connection = GetConnection(connectionId);
//             foreach (var path in paths)
//             {
//                 // if (...) - нужно понять, директория ли это. Если да, то надо удалить как директорию
//                 // else
//                 connection.DeleteFile(path);
//             }
//             return Ok();
//         }
//         catch (Exception e)
//         {
//             logger.LogError(e, $"Error while deleting paths (connectionId {connectionId})");
//             return BadRequest();
//         }
//     }

//     [HttpPost("rename")]
//     public IActionResult Rename(Guid connectionId, [FromBody] RenameRequest request)
//     {
//         logger.LogInformation($"Renaming file or directory (connectionId {connectionId})");
//         try
//         {
//             var connection = GetConnection(connectionId);
//             // if (...) - нужно понять, директория ли это
//             // else 
//             connection.RenameFile(request.oldPath, request.newPath);
//             return Ok();
//         }
//         catch (Exception e)
//         {
//             logger.LogError(e, $"Error while renaming file or directory (connectionId {connectionId})");
//             return BadRequest();
//         }
//     }

//     [HttpPost("copy")]
//     public IActionResult Copy(Guid connectionId, [FromBody] RenameRequest request)
//     {
//         logger.LogInformation($"Copying file or directory (connectionId {connectionId})");
//         throw new NotImplementedException();
//         try
//         {

//             return Ok();
//         }
//         catch (Exception e)
//         {
//             logger.LogError(e, $"Error while copying file or directory (connectionId {connectionId})");
//             return BadRequest();
//         }
//     }

//     [HttpPost("move")]
//     public IActionResult Move(Guid connectionId, [FromBody] MoveRequest request)
//     {
//         logger.LogInformation($"Moving file or directory (connectionId {connectionId})");
//         throw new NotImplementedException();
//         try
//         {
//             var connection = GetConnection(connectionId);
//             foreach (var path in request.sourcePaths)
//             {

//             }
//             return Ok();
//         }
//         catch (Exception e)
//         {
//             logger.LogError(e, $"Error while moving file or directory (connectionId {connectionId})");
//             return BadRequest();
//         }
//     }

//     [HttpPost("upload")]
//     public IActionResult Upload(Guid connectionId, [FromBody] UploadRequest request)
//     {
//         logger.LogInformation($"Uploading file (connectionId {connectionId})");
//         try
//         {
//             var connection = GetConnection(connectionId);
//             byte[] bytes = Convert.FromBase64String(request.base64String);
//             connection.SaveFile(request.remotePath, new MemoryStream(bytes));
//             return Ok();
//         }
//         catch (Exception e)
//         {
//             logger.LogError(e, $"Error while uploading file (connectionId {connectionId})");
//             return BadRequest();
//         }
//     }

//     [HttpPost("download")]
//     public IActionResult Download(Guid connectionId, [FromBody] string path)
//     {
//         logger.LogInformation($"Downloading file (connectionId {connectionId})");
//         try
//         {
//             var connection = GetConnection(connectionId);
//             var stream = connection.GetFile(path);
//             byte[] bytes = new byte[stream.Length];
//             stream.ReadExactly(bytes, 0, bytes.Count());
//             return Ok(Convert.ToBase64String(bytes));
//         }
//         catch (Exception e)
//         {
//             logger.LogError(e, $"Error while downloading file or directory (connectionId {connectionId})");
//             return BadRequest();
//         }
//     }
// }
