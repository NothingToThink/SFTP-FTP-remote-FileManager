using Core.Interfaces.Protocol;
using Core.Models;

namespace Core.Implementations.Protocol;

public class LocalConnection : IConnection
{
    private readonly string _rootPath;
    private string _currentPath = "";
    private bool _isConnected = false;

    public LocalConnection(string rootFolder = "tmp")
    {
        _rootPath = Path.GetFullPath(rootFolder);
        if (!Directory.Exists(_rootPath))
        {
            Directory.CreateDirectory(_rootPath);
        }
    }

    private string GetLocalPath (string remotePath)
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

    public Task<OperationStatus> Connect()
    {
        _isConnected = true;
        return Task.FromResult(new OperationStatus
        {
            Code = 0, 
            Message = "connected",
            IsSuccess = true
        });
    }

    public Task<OperationStatus> Disconnect()
    {
        _isConnected = false;
        return Task.FromResult(new OperationStatus
        {
            Code = 0, 
            Message = "disconnected",
            IsSuccess = true
        });
    }

    public bool IsConnected  => _isConnected;

    public async Task<OperationStatus> SaveFile (string remotePath, Stream content)
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
                useAsync: true))
            {
                await content.CopyToAsync(fileStream);
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

    public Task<OperationStatus> CreateFile (string remotePath)
    {
        try
        {
            var localPath = GetLocalPath(remotePath);
            File.Create(localPath).Dispose();
            return Task.FromResult(new OperationStatus
            {
                Code = 0, 
                Message = "file created",
                IsSuccess = true
            });
        }   
        catch (Exception e)
        {
            return Task.FromResult(new OperationStatus
            {
                Code = 1, 
                Message = e.Message + " file not created",
                IsSuccess = false
            });
        }
    }

    public Task<OperationStatus> DeleteFile (string remotePath)
    {
        try
        {
            var localPath = GetLocalPath(remotePath);
            if (!File.Exists(localPath))
            {
                throw new ArgumentException($"file {remotePath} not exists");
            }
            File.Delete(localPath);
            return Task.FromResult(new OperationStatus
            {
                Code = 0, 
                Message = "file deleted",
                IsSuccess = true
            });
        }   
        catch (Exception e)
        {
            return Task.FromResult(new OperationStatus
            {
                Code = 1, 
                Message = e.Message + " file not deleted",
                IsSuccess = false
            });
        }
    }

    public Task<OperationStatus> RenameFile (string oldName, string newName)
    {
        try
        {
            var oldPath = GetLocalPath(oldName);
            var newPath = GetLocalPath(newName);
            File.Move(oldPath, newPath);
            return Task.FromResult(new OperationStatus
            {
                Code = 0, 
                Message = "file renamed",
                IsSuccess = true
            });
        }   
        catch (Exception e)
        {
            return Task.FromResult(new OperationStatus
            {
                Code = 1, 
                Message = e.Message + " file not renamed",
                IsSuccess = false
            });
        }
    }

    public Task<OperationStatus> CreateDir (string remotePath)
    {
        try
        {
            var localPath = GetLocalPath(remotePath);
            Directory.CreateDirectory(localPath);
            return Task.FromResult(new OperationStatus
            {
                Code = 0, 
                Message = "directory created",
                IsSuccess = true
            });
        }   
        catch (Exception e)
        {
            return Task.FromResult(new OperationStatus
            {
                Code = 1, 
                Message = e.Message + " directory not created",
                IsSuccess = false
            });
        }
    }

    public Task<OperationStatus> DeleteDir (string remotePath)
    {
        try
        {
            var localPath = GetLocalPath(remotePath);
            if (!Directory.Exists(localPath))
            {
                throw new ArgumentException($"directory {remotePath} not exists");
            }
            Directory.Delete(localPath, recursive: true);
            return Task.FromResult(new OperationStatus
            {
                Code = 0, 
                Message = "directory removed",
                IsSuccess = true
            });
        }   
        catch (Exception e)
        {
            return Task.FromResult(new OperationStatus
            {
                Code = 1, 
                Message = e.Message + " directory not removed",
                IsSuccess = false
            });
        }
    }

    public Task<OperationStatus> RenameDir (string oldName, string newName)
    {
        try
        {
            var oldPath = GetLocalPath(oldName);
            var newPath = GetLocalPath(newName);
            Directory.Move(oldPath, newPath);
            return Task.FromResult(new OperationStatus
            {
                Code = 0, 
                Message = "directory renamed",
                IsSuccess = true
            });
        }   
        catch (Exception e)
        {
            return Task.FromResult(new OperationStatus
            {
                Code = 1, 
                Message = e.Message + " directory not renamed",
                IsSuccess = false
            });
        }
    }

    public Task<OperationStatus> ChangeDirectory (string path)
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
            return Task.FromResult(new OperationStatus
            {
                Code = 0, 
                Message = "directory changed",
                IsSuccess = true
            });
        }
        catch (Exception e)
        {
            return Task.FromResult(new OperationStatus
            {
                Code = 1, 
                Message = e.Message + " directory not changed",
                IsSuccess = false
            });
        }
    }

    public Task<OperationStatus> ChangeFile(string path)
    {
        return Task.FromResult(new OperationStatus
        {
            Code = 0, 
            Message = "file changed",
            IsSuccess = true
        });
    }

    public Task<QueryResult<List<FileItem>>> GetFiles(string path)
    {
        try {
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

            return Task.FromResult(new QueryResult<List<FileItem>>
            {
                Data = data,
                Status = new OperationStatus
                {
                    Code = 0, 
                    Message = "files received",
                    IsSuccess = true
                }
            });
        }
        catch (Exception e)
        {
            return Task.FromResult(new QueryResult<List<FileItem>>
            {
                Status = new OperationStatus
                {
                    Code = 1, 
                    Message = e.Message + " files not received",
                    IsSuccess = false
                }
            });
        }
    }

    private static string GetPermissionsString (FileSystemInfo info)
    {
        var attrs = info.Attributes;
        bool isDir    = attrs.HasFlag(FileAttributes.Directory);
        bool isReadOnly = attrs.HasFlag(FileAttributes.ReadOnly);

        char r = 'r';
        char w = isReadOnly ? '-' : 'w';
        char x = isDir ? 'x' : '-';

        return $"{r}{w}{x}{r}{w}{x}{r}{w}{x}";
    }

    public Task<QueryResult<Stream>> GetFile(string path)
    {
        try
        {
            string localPath = GetLocalPath(path);

            var data = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            return Task.FromResult(new QueryResult<Stream>
            {
                Data = data,
                Status = new OperationStatus
                {
                    Code = 0, 
                    Message = "file recieved",
                    IsSuccess = true
                }
            });
        }
        catch (Exception e)
        {
            return Task.FromResult(new QueryResult<Stream>
            {
                Status = new OperationStatus
                {
                    Code = 1, 
                    Message = e.Message + " file not received",
                    IsSuccess = false
                }
            });
        }
    }

    public Task<QueryResult<List<string>>> GetDirectories(string path)
    {
        try
        {
            var localPath = GetLocalPath(path);

            var dirInfo = new DirectoryInfo(localPath);

            var data = dirInfo.GetDirectories()
            .Select(info => info.Name)
            .ToList();

            return Task.FromResult(new QueryResult<List<string>>
            {
                Data = data,
                Status = new OperationStatus
                {
                    Code = 0, 
                    Message = "directories received",
                    IsSuccess = true
                }
            });
        }
        catch (Exception e)
        {
            return Task.FromResult(new QueryResult<List<string>>
            {
                Status = new OperationStatus
                {
                    Code = 1, 
                    Message = e.Message + " directories not received",
                    IsSuccess = false
                }
            });
        }
    }
}