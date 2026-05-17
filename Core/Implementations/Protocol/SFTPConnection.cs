using Core.Interfaces.Protocol;
using Core.Models;

using Renci.SshNet;
using Renci.SshNet.Sftp;

namespace Core.Implementations.Protocol;

public class SftpConnection : IConnection
{
    private SftpClient _client;

    public SftpConnection(HostProfile profile)
    {
        _client = new SftpClient(profile.Host, profile.Port,
                profile.AuthData.Username,
                profile.AuthData.Password ?? throw new InvalidOperationException("Only password auth nowadays")
            );
    }

    public Task<OperationStatus> Connect()
    {
        try
        {
            _client.Connect();
            return Task.FromResult(new OperationStatus
            {
                Code = 0,
                Message = "Connected"
            });
        }
        catch (Exception e)
        {
            return Task.FromResult(new OperationStatus
            {
                Code = 1,
                Message = e.Message + " Connection failed"
            });
        }
    }

    public Task<OperationStatus> Disconnect()
    {
        try
        {
            if (_client.IsConnected)
            {
                _client.Disconnect();
            }

            return Task.FromResult(new OperationStatus
            {
                Code = 0,
                Message = "Disconnected successfully"
            });
        }
        catch (Exception e)
        {
            return Task.FromResult(new OperationStatus
            {
                Code = 1,
                Message = e.Message + " Disconnection failed"
            });
        }
    }

    public bool IsConnected => _client.IsConnected;

    public Task<QueryResult<List<FileItem>>> GetFiles(string path)
    {
        try
        {
            var files = _client.ListDirectory(path)
                .Where(f => f.Name != "." && f.Name != "..") // в Filezilla точка передается, хз надо ли нам 
                .Select(file => new FileItem
                {
                    Name = file.Name,
                    Size = file.Length,
                    LastModified = file.LastWriteTime,
                    IsDirectory = file.IsDirectory,
                    Permissions = GetPermissionsString(file.Attributes),
                }).ToList();
            return Task.FromResult(new QueryResult<List<FileItem>>
            {
                Data = files,
                Status = new OperationStatus { Code = 0, Message = "All files received" }
            });
        }
        catch (Exception e)
        {
            return Task.FromResult(new QueryResult<List<FileItem>>
            {
                Data = null,
                Status = new OperationStatus { Code = 1, Message = e.Message + " Failed to receive files" }
            });
        }
    }

    public Task<QueryResult<Stream>> GetFile(string path)
    {
        try
        {
            var memoryStream = new MemoryStream();
            _client.DownloadFile(path, memoryStream);
            memoryStream.Position = 0;

            return Task.FromResult(new QueryResult<Stream>
            {
                Data = memoryStream,
                Status = new OperationStatus { Code = 0, Message = "File downloaded" }
            });
        }
        catch (Exception e)
        {
            return Task.FromResult(new QueryResult<Stream>
            {
                Data = null,
                Status = new OperationStatus { Code = 1, Message = e.Message + " Failed to download file" }
            });
        }
    }

    public Task<QueryResult<List<string>>> GetDirectories(string path)
    {
        try
        {
            var dirs = _client.ListDirectory(path)
                .Where(f => f.IsDirectory && f.Name != "." && f.Name != "..")
                .Select(f => f.FullName)
                .ToList();

            return Task.FromResult(new QueryResult<List<string>>
            {
                Data = dirs,
                Status = new OperationStatus { Code = 0, Message = "Directories received" }
            });
        }
        catch (Exception e)
        {
            return Task.FromResult(new QueryResult<List<string>>
            {
                Data = null,
                Status = new OperationStatus { Code = 1, Message = e.Message + " Failed to receive directories" }
            });
        }
    }

    public Task<QueryResult<string>> GetWorkingDirectory()
    {
        try
        {
            return Task.FromResult(new QueryResult<string>
            {
                Data = _client.WorkingDirectory,
                Status = new OperationStatus { Code = 0, Message = "Working direcotory recieved" }
            });
        }
        catch (Exception e)
        {
            return Task.FromResult(new QueryResult<string>
            {
                Data = null,
                Status = new OperationStatus { Code = 1, Message = e.Message + " Failed to receive working directory" }
            });
        }
    }

    public Task<OperationStatus> SaveFile(string remotePath, Stream content)
    {
        try
        {
            if (content.CanSeek) content.Position = 0;
            _client.UploadFile(content, remotePath, true);

            return Task.FromResult(new OperationStatus
            {
                Code = 0,
                Message = "File saved successfully"
            });
        }
        catch (Exception e)
        {
            return Task.FromResult(new OperationStatus
            {
                Code = 1,
                Message = e.Message + " Failed to save file"
            });
        }
    }

    public Task<OperationStatus> CreateFile(string remotePath)
    {
        try
        {
            using var stream = _client.Create(remotePath);
            return Task.FromResult(new OperationStatus
            {
                Code = 0,
                Message = "File created"
            });
        }
        catch (Exception e)
        {
            return Task.FromResult(new OperationStatus
            {
                Code = 1,
                Message = e.Message + " Failed to create file"
            });
        }
    }

    public Task<OperationStatus> DeleteFile(string remotePath)
    {
        try
        {
            _client.DeleteFile(remotePath);
            return Task.FromResult(new OperationStatus
            {
                Code = 0,
                Message = "File deleted"
            });
        }
        catch (Exception e)
        {
            return Task.FromResult(new OperationStatus
            {
                Code = 1,
                Message = e.Message + " Failed to delete file"
            });
        }
    }

    public Task<OperationStatus> RenameFile(string oldName, string newName)
    {
        try
        {
            _client.RenameFile(oldName, newName);
            return Task.FromResult(new OperationStatus
            {
                Code = 0,
                Message = "File renamed"
            });
        }
        catch (Exception e)
        {
            return Task.FromResult(new OperationStatus
            {
                Code = 1,
                Message = e.Message + " Failed to rename file"
            });
        }
    }

    public Task<OperationStatus> CreateDir(string remotePath)
    {
        try
        {
            _client.CreateDirectory(remotePath);
            return Task.FromResult(new OperationStatus
            {
                Code = 0,
                Message = "Directory created"
            });
        }
        catch (Exception e)
        {
            return Task.FromResult(new OperationStatus
            {
                Code = 1,
                Message = e.Message + " Failed to create directory"
            });
        }
    }

    public Task<OperationStatus> DeleteDir(string remotePath)
    {
        try
        {
            DeleteDirectoryRecursive(remotePath);
            return Task.FromResult(new OperationStatus
            {
                Code = 0,
                Message = "Directory deleted"
            });
        }
        catch (Exception e)
        {
            return Task.FromResult(new OperationStatus
            {
                Code = 1,
                Message = e.Message + " Failed to delete directory"
            });
        }
    }

    private void DeleteDirectoryRecursive(string path)
    {
        foreach (var entry in _client.ListDirectory(path))
        {
            if (entry.Name is "." or "..") continue;

            if (entry.IsDirectory)
                DeleteDirectoryRecursive(entry.FullName);
            else
                _client.DeleteFile(entry.FullName);
        }
        _client.DeleteDirectory(path);
    }

    public Task<OperationStatus> RenameDir(string oldName, string newName)
    {
        try
        {
            _client.RenameFile(oldName, newName);
            return Task.FromResult(new OperationStatus
            {
                Code = 0,
                Message = "Directory renamed"
            });
        }
        catch (Exception e)
        {
            return Task.FromResult(new OperationStatus
            {
                Code = 1,
                Message = e.Message + " Failed to rename directory"
            });
        }
    }

    public Task<OperationStatus> ChangeDirectory(string path)
    {
        try
        {
            _client.ChangeDirectory(path);
            return Task.FromResult(new OperationStatus
            {
                Code = 0,
                Message = $"Changed directory to {_client.WorkingDirectory}"
            });
        }
        catch (Exception e)
        {
            return Task.FromResult(new OperationStatus
            {
                Code = 1,
                Message = e.Message + " Failed to change directory"
            });
        }
    }

    public Task<OperationStatus> ChangeFile(string path)
    {
        try
        {
            if (!_client.Exists(path))
            {
                return Task.FromResult(new OperationStatus
                {
                    Code = 1,
                    Message = "File does not exist"
                });
            }

            var attrs = _client.GetAttributes(path);
            if (attrs.IsDirectory)
            {
                return Task.FromResult(new OperationStatus
                {
                    Code = 1,
                    Message = "Path is a directory, not a file"
                });
            }

            return Task.FromResult(new OperationStatus
            {
                Code = 0,
                Message = $"File selected: {path}"
            });
        }
        catch (Exception e)
        {
            return Task.FromResult(new OperationStatus
            {
                Code = 1,
                Message = e.Message + " Failed to access file"
            });
        }
    }

    private static string GetPermissionsString(SftpFileAttributes attrs)
    {
        return ""
               + (attrs.OwnerCanRead ? "r" : "-")
               + (attrs.OwnerCanWrite ? "w" : "-")
               + (attrs.OwnerCanExecute ? "x" : "-")
               + (attrs.GroupCanRead ? "r" : "-")
               + (attrs.GroupCanWrite ? "w" : "-")
               + (attrs.GroupCanExecute ? "x" : "-")
               + (attrs.OthersCanRead ? "r" : "-")
               + (attrs.OthersCanWrite ? "w" : "-")
               + (attrs.OthersCanExecute ? "x" : "-");
    }
}