using Core.Interfaces.Protocol;
using Core.Models;

namespace Core.Implementations.Protocol;

public class LocalConnection : Connection
{
    private readonly string _rootPath;
    private string _currentPath = "";
    private bool _isConnected;

    public LocalConnection(string rootFolder = "tmp")
    {
        _rootPath = Path.GetFullPath(rootFolder);

        if (!Directory.Exists(_rootPath))
        {
            Directory.CreateDirectory(_rootPath);
        }
    }

    private string GetLocalPath(string remotePath)
    {
        string combined;

        if (remotePath.StartsWith('\\') || remotePath.StartsWith('/'))
        {
            combined = Path.Combine(_rootPath, remotePath.TrimStart('/', '\\'));
        }
        else
        {
            combined = Path.Combine(_rootPath, _currentPath, remotePath);
        }

        var fullPath = Path.GetFullPath(combined);

        if (!fullPath.StartsWith(_rootPath))
        {
            throw new AccessViolationException($"All remote paths must be in {_rootPath} directory");
        }

        return fullPath;
    }

    public override OperationStatus Connect()
    {
        _isConnected = true;

        return new OperationStatus
        {
            Code = 0,
            Message = "connected",
            IsSuccess = true
        };
    }

    public override OperationStatus Disconnect()
    {
        _isConnected = false;

        return new OperationStatus
        {
            Code = 0,
            Message = "disconnected",
            IsSuccess = true
        };
    }

    public override bool IsConnected => _isConnected;

    public override OperationStatus SaveFile(string remotePath, Stream content)
    {
        try
        {
            var localPath = GetLocalPath(remotePath);

            Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);

            using (var fileStream = new FileStream(
                       localPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       useAsync: false))
            {
                content.CopyTo(fileStream);
            }

            return new OperationStatus
            {
                Code = 0,
                Message = "file saved",
                IsSuccess = true
            };
        }
        catch (Exception e)
        {
            return new OperationStatus
            {
                Code = 1,
                Message = e.Message + "file not saved",
                IsSuccess = false
            };
        }
    }

    public override OperationStatus CreateFile(string remotePath)
    {
        try
        {
            var localPath = GetLocalPath(remotePath);

            File.Create(localPath).Dispose();

            return new OperationStatus
            {
                Code = 0,
                Message = "file created",
                IsSuccess = true
            };
        }
        catch (Exception e)
        {
            return new OperationStatus
            {
                Code = 1,
                Message = e.Message + " file not created",
                IsSuccess = false
            };
        }
    }

    public override OperationStatus DeleteFile(string remotePath)
    {
        try
        {
            var localPath = GetLocalPath(remotePath);

            if (!File.Exists(localPath))
            {
                throw new ArgumentException($"file {remotePath} not exists");
            }

            File.Delete(localPath);

            return new OperationStatus
            {
                Code = 0,
                Message = "file deleted",
                IsSuccess = true
            };
        }
        catch (Exception e)
        {
            return new OperationStatus
            {
                Code = 1,
                Message = e.Message + " file not deleted",
                IsSuccess = false
            };
        }
    }

    public override OperationStatus RenameFile(string oldName, string newName)
    {
        try
        {
            var oldPath = GetLocalPath(oldName);
            var newPath = GetLocalPath(newName);

            File.Move(oldPath, newPath);

            return new OperationStatus
            {
                Code = 0,
                Message = "file renamed",
                IsSuccess = true
            };
        }
        catch (Exception e)
        {
            return new OperationStatus
            {
                Code = 1,
                Message = e.Message + " file not renamed",
                IsSuccess = false
            };
        }
    }

    public override OperationStatus CreateDir(string remotePath)
    {
        try
        {
            var localPath = GetLocalPath(remotePath);

            Directory.CreateDirectory(localPath);

            return new OperationStatus
            {
                Code = 0,
                Message = "directory created",
                IsSuccess = true
            };
        }
        catch (Exception e)
        {
            return new OperationStatus
            {
                Code = 1,
                Message = e.Message + " directory not created",
                IsSuccess = false
            };
        }
    }

    public override OperationStatus DeleteDir(string remotePath)
    {
        try
        {
            var localPath = GetLocalPath(remotePath);

            if (!Directory.Exists(localPath))
            {
                throw new ArgumentException($"directory {remotePath} not exists");
            }

            Directory.Delete(localPath, recursive: true);

            return new OperationStatus
            {
                Code = 0,
                Message = "directory removed",
                IsSuccess = true
            };
        }
        catch (Exception e)
        {
            return new OperationStatus
            {
                Code = 1,
                Message = e.Message + " directory not removed",
                IsSuccess = false
            };
        }
    }

    public override OperationStatus RenameDir(string oldName, string newName)
    {
        try
        {
            var oldPath = GetLocalPath(oldName);
            var newPath = GetLocalPath(newName);

            Directory.Move(oldPath, newPath);

            return new OperationStatus
            {
                Code = 0,
                Message = "directory renamed",
                IsSuccess = true
            };
        }
        catch (Exception e)
        {
            return new OperationStatus
            {
                Code = 1,
                Message = e.Message + " directory not renamed",
                IsSuccess = false
            };
        }
    }

    public override OperationStatus ChangeDirectory(string path)
    {
        try
        {
            var fullPath = GetLocalPath(path);

            if (!Directory.Exists(fullPath))
            {
                throw new ArgumentException($"directory {path} not exists");
            }

            var newCurrentPath = Path.GetRelativePath(_rootPath, fullPath);

            _currentPath = (newCurrentPath == ".") ? "" : newCurrentPath;

            return new OperationStatus
            {
                Code = 0,
                Message = "directory changed",
                IsSuccess = true
            };
        }
        catch (Exception e)
        {
            return new OperationStatus
            {
                Code = 1,
                Message = e.Message + " directory not changed",
                IsSuccess = false
            };
        }
    }

    public override OperationStatus ChangeFile(string path)
    {
        return new OperationStatus
        {
            Code = 0,
            Message = "file changed",
            IsSuccess = true
        };
    }

    public override QueryResult<List<FileItem>> GetFiles(string path)
    {
        try
        {
            var localPath = GetLocalPath(path);

            var dirInfo = new DirectoryInfo(localPath);

            var data = dirInfo.GetFileSystemInfos()
                .Select(info => new FileItem
                {
                    Name = info.Name,
                    Size = (info is FileInfo f) ? f.Length : 0,
                    LastModified = info.LastWriteTime,
                    IsDirectory = info is DirectoryInfo,
                    FullPath = Path.GetRelativePath(_rootPath, info.FullName),
                    Permissions = GetPermissionsString(info)
                })
                .ToList();

            return new QueryResult<List<FileItem>>
            {
                Data = data,
                Status = new OperationStatus
                {
                    Code = 0,
                    Message = "files received",
                    IsSuccess = true
                }
            };
        }
        catch (Exception e)
        {
            return new QueryResult<List<FileItem>>
            {
                Status = new OperationStatus
                {
                    Code = 1,
                    Message = e.Message + " files not received",
                    IsSuccess = false
                }
            };
        }
    }

    private static string GetPermissionsString(FileSystemInfo info)
    {
        var attrs = info.Attributes;

        bool isDir = attrs.HasFlag(FileAttributes.Directory);
        bool isReadOnly = attrs.HasFlag(FileAttributes.ReadOnly);

        char r = 'r';
        char w = isReadOnly ? '-' : 'w';
        char x = isDir ? 'x' : '-';

        return $"{r}{w}{x}{r}{w}{x}{r}{w}{x}";
    }

    public override QueryResult<Stream> GetFile(string path)
    {
        try
        {
            string localPath = GetLocalPath(path);

            var data = new FileStream(
                localPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                useAsync: false);

            return new QueryResult<Stream>
            {
                Data = data,
                Status = new OperationStatus
                {
                    Code = 0,
                    Message = "file received",
                    IsSuccess = true
                }
            };
        }
        catch (Exception e)
        {
            return new QueryResult<Stream>
            {
                Status = new OperationStatus
                {
                    Code = 1,
                    Message = e.Message + " file not received",
                    IsSuccess = false
                }
            };
        }
    }

    public override QueryResult<List<string>> GetDirectories(string path)
    {
        try
        {
            var localPath = GetLocalPath(path);

            var dirInfo = new DirectoryInfo(localPath);

            var data = dirInfo.GetDirectories()
                .Select(info => info.Name)
                .ToList();

            return new QueryResult<List<string>>
            {
                Data = data,
                Status = new OperationStatus
                {
                    Code = 0,
                    Message = "directories received",
                    IsSuccess = true
                }
            };
        }
        catch (Exception e)
        {
            return new QueryResult<List<string>>
            {
                Status = new OperationStatus
                {
                    Code = 1,
                    Message = e.Message + " directories not received",
                    IsSuccess = false
                }
            };
        }
    }

    public override QueryResult<string> GetWorkingDirectory()
    {
        return new QueryResult<string>
        {
            Data = _currentPath,
            Status = new OperationStatus
            {
                Code = 0,
                Message = "Working directory received"
            }
        };
    }

    protected override void DisposeCore()
    {

    }
}