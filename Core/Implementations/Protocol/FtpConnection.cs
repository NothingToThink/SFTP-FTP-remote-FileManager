using System.Net;
using Core.Interfaces.Protocol;
using Core.Models;
using Core.Models.Credentials;
using FluentFTP;

namespace Core.Implementations.Protocol;

public class FtpConnection : Connection
{
    private readonly FtpClient _client;

    public FtpConnection(HostProfile profile)
    {
        _client = profile.Auth switch
        {
            PasswordAuth(var user, var pwd) 
                => new FtpClient(profile.Host, new NetworkCredential(user, pwd), profile.EffectivePort),
            
            AnonymousAuth 
                => new FtpClient(profile.Host, new NetworkCredential("anonymous", "anonymous@example.com"), profile.EffectivePort),
            
            KeyAuth 
                => throw new InvalidOperationException("Key authentication is not supported by FTP. Use SFTP instead."),
            
            _ => throw new ArgumentOutOfRangeException(nameof(profile.Auth))
        };
    }
    
    public override OperationStatus Connect()
    {
        try
        {
            _client.Connect();
            return new OperationStatus
            {
                Code = 0,
                Message = "Connected"
            };
        }
        catch (Exception e)
        {
            return new OperationStatus
            {
                Code = 1,
                Message = e.Message + " Connection failed"
            };
        }
    }

    public override OperationStatus Disconnect()
    {
        try
        {
            if (_client is { IsConnected: true })
            {
                _client.Disconnect();
                _client.Dispose();
            }

            return new OperationStatus
            {
                Code = 0,
                Message = "Disconnected successfully"
            };
        }
        catch (Exception e)
        {
            return new OperationStatus
            {
                Code = 1,
                Message = e.Message + " Disconnection failed"
            };
        }
    }

    public override bool IsConnected => _client.IsConnected;

    public override QueryResult<List<FileItem>> GetFiles(string path)
    {
        try
        {
            var files = _client.GetListing(path)
                .Where(f => f.Name != "." && f.Name != "..")
                .Select(file => new FileItem
                {
                    Name = file.Name,
                    Size = file.Size,
                    LastModified = file.Modified,
                    IsDirectory = file.Type == FtpObjectType.Directory,
                    Permissions = GetPermissionsString(file.Chmod),
                }).ToList();

            return new QueryResult<List<FileItem>>
            {
                Data = files,
                Status = new OperationStatus { Code = 0, Message = "All files received" }
            };
        }
        catch (Exception e)
        {
            return new QueryResult<List<FileItem>>
            {
                Data = null,
                Status = new OperationStatus { Code = 1, Message = e.Message + " Failed to receive files" }
            };
        }
    }

    public override QueryResult<Stream> GetFile(string path)
    {
        try
        {
            using var ftpStream = _client.OpenRead(path);
            var memoryStream = new MemoryStream();
            ftpStream.CopyTo(memoryStream);
            memoryStream.Position = 0;

            return new QueryResult<Stream>
            {
                Data = memoryStream,
                Status = new OperationStatus { Code = 0, Message = "File downloaded" }
            };
        }
        catch (Exception e)
        {
            return new QueryResult<Stream>
            {
                Data = null,
                Status = new OperationStatus { Code = 1, Message = e.Message + " Failed to download file" }
            };
        }
    }
    
    public override QueryResult<List<string>> GetDirectories(string path)
    {
        try
        {
            var dirs = _client.GetListing(path)
                .Where(f => f.Type == FtpObjectType.Directory && f.Name != "." && f.Name != "..")
                .Select(f => f.FullName)
                .ToList();

            return new QueryResult<List<string>>
            {
                Data = dirs,
                Status = new OperationStatus { Code = 0, Message = "Directories received" }
            };
        }
        catch (Exception e)
        {
            return new QueryResult<List<string>>
            {
                Data = null,
                Status = new OperationStatus { Code = 1, Message = e.Message + " Failed to receive directories" }
            };
        }
    }

    public override QueryResult<string> GetWorkingDirectory()
    {
        try
        {
            return new QueryResult<string>
            {
                Data = _client.GetWorkingDirectory(),
                Status = new OperationStatus { Code = 0, Message = "Working directory received" }
            };
        }
        catch (Exception e)
        {
            return new QueryResult<string>
            {
                Data = null,
                Status = new OperationStatus { Code = 1, Message = e.Message + " Failed to receive working directory" }
            };
        }
    }
    
    public override OperationStatus SaveFile(string remotePath, Stream content)
    {
        try
        {
            if (content.CanSeek) content.Position = 0;
            _client.UploadStream(content, remotePath);

            return new OperationStatus
            {
                Code = 0,
                Message = "File saved successfully"
            };
        }
        catch (Exception e)
        {
            return new OperationStatus
            {
                Code = 1,
                Message = e.Message + " Failed to save file"
            };
        }
    }

    public override OperationStatus CreateFile(string remotePath)
    {
        try
        {
            using var stream = _client.OpenWrite(remotePath);
            stream.Close();

            return new OperationStatus
            {
                Code = 0,
                Message = "File created"
            };
        }
        catch (Exception e)
        {
            return new OperationStatus
            {
                Code = 1,
                Message = e.Message + " Failed to create file"
            };
        }
    }

    public override OperationStatus DeleteFile(string remotePath)
    {
        try
        {
            _client.DeleteFile(remotePath);

            return new OperationStatus
            {
                Code = 0,
                Message = "File deleted"
            };
        }
        catch (Exception e)
        {
            return new OperationStatus
            {
                Code = 1,
                Message = e.Message + " Failed to delete file"
            };
        }
    }

    public override OperationStatus RenameFile(string oldName, string newName)
    {
        try
        {
            _client.Rename(oldName, newName);

            return new OperationStatus
            {
                Code = 0,
                Message = "File renamed"
            };
        }
        catch (Exception e)
        {
            return new OperationStatus
            {
                Code = 1,
                Message = e.Message + " Failed to rename file"
            };
        }
    }

    public override OperationStatus CreateDir(string remotePath)
    {
        try
        {
            _client.CreateDirectory(remotePath);

            return new OperationStatus
            {
                Code = 0,
                Message = "Directory created"
            };
        }
        catch (Exception e)
        {
            return new OperationStatus
            {
                Code = 1,
                Message = e.Message + " Failed to create directory"
            };
        }
    }

    public override OperationStatus DeleteDir(string remotePath)
    {
        try
        {
            DeleteDirectoryRecursive(remotePath);

            return new OperationStatus
            {
                Code = 0,
                Message = "Directory deleted"
            };
        }
        catch (Exception e)
        {
            return new OperationStatus
            {
                Code = 1,
                Message = e.Message + " Failed to delete directory"
            };
        }
    }

    private void DeleteDirectoryRecursive(string path)
    {
        foreach (var entry in _client.GetListing(path))
        {
            if (entry.Name is "." or "..") continue;

            if (entry.Type == FtpObjectType.Directory)
                DeleteDirectoryRecursive(entry.FullName);
            else
                _client.DeleteFile(entry.FullName);
        }

        _client.DeleteDirectory(path);
    }

    public override OperationStatus RenameDir(string oldName, string newName)
    {
        try
        {
            _client.Rename(oldName, newName);

            return new OperationStatus
            {
                Code = 0,
                Message = "Directory renamed"
            };
        }
        catch (Exception e)
        {
            return new OperationStatus
            {
                Code = 1,
                Message = e.Message + " Failed to rename directory"
            };
        }
    }

    public override OperationStatus ChangeDirectory(string path)
    {
        try
        {
            _client.SetWorkingDirectory(path);

            return new OperationStatus
            {
                Code = 0,
                Message = $"Changed directory to {_client.GetWorkingDirectory()}"
            };
        }
        catch (Exception e)
        {
            return new OperationStatus
            {
                Code = 1,
                Message = e.Message + " Failed to change directory"
            };
        }
    }
    
    public override OperationStatus ChangeFile(string path)
    {
        try
        {
            if (!_client.FileExists(path))
            {
                return new OperationStatus
                {
                    Code = 1,
                    Message = "File does not exist"
                };
            }

            if (_client.GetObjectInfo(path).Type == FtpObjectType.Directory)
            {
                return new OperationStatus
                {
                    Code = 1,
                    Message = "Path is a directory, not a file"
                };
            }

            return new OperationStatus
            {
                Code = 0,
                Message = $"File selected: {path}"
            };
        }
        catch (Exception e)
        {
            return new OperationStatus
            {
                Code = 1,
                Message = e.Message + " Failed to access file"
            };
        }
    }

    private static string GetPermissionsString(int chmod)
    {
        bool ownerRead = (chmod & 0x100) != 0;
        bool ownerWrite = (chmod & 0x80) != 0;
        bool ownerExecute = (chmod & 0x40) != 0;

        bool groupRead = (chmod & 0x20) != 0;
        bool groupWrite = (chmod & 0x10) != 0;
        bool groupExecute = (chmod & 0x08) != 0;

        bool othersRead = (chmod & 0x04) != 0;
        bool othersWrite = (chmod & 0x02) != 0;
        bool othersExecute = (chmod & 0x01) != 0;

        return ""
               + (ownerRead ? "r" : "-")
               + (ownerWrite ? "w" : "-")
               + (ownerExecute ? "x" : "-")
               + (groupRead ? "r" : "-")
               + (groupWrite ? "w" : "-")
               + (groupExecute ? "x" : "-")
               + (othersRead ? "r" : "-")
               + (othersWrite ? "w" : "-")
               + (othersExecute ? "x" : "-");
    }

    protected override void DisposeCore()
    {
        _client.Dispose();
    }
}