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

    public override bool IsConnected => _isConnected;

    public override Task ConnectAsync(CancellationToken ct = default)
    {
        _isConnected = true;
        return Task.CompletedTask;
    }

    public override Task DisconnectAsync(CancellationToken ct = default)
    {
        _isConnected = false;
        return Task.CompletedTask;
    }

    public override async Task SaveFileAsync(string remotePath, Stream content, CancellationToken ct = default)
    {
        var localPath = GetLocalPath(remotePath);
        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
        if (content.CanSeek) content.Position = 0;
        await using var fileStream = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await content.CopyToAsync(fileStream, ct);
    }

    public override Task CreateFileAsync(string remotePath, CancellationToken ct = default)
    {
        var localPath = GetLocalPath(remotePath);
        File.Create(localPath).Dispose();
        return Task.CompletedTask;
    }

    public override Task DeleteFileAsync(string remotePath, CancellationToken ct = default)
    {
        var localPath = GetLocalPath(remotePath);

        if (!File.Exists(localPath))
        {
            throw new ArgumentException($"file {remotePath} not exists");
        }

        File.Delete(localPath);
        return Task.CompletedTask;
    }

    public override Task RenameFileAsync(string oldName, string newName, CancellationToken ct = default)
    {
        var oldPath = GetLocalPath(oldName);
        var newPath = GetLocalPath(newName);

        File.Move(oldPath, newPath);
        return Task.CompletedTask;
    }

    public override Task MoveFileAsync(string sourcePath, string targetPath, bool canOverride = true, CancellationToken ct = default)
    {
        var localSource = GetLocalPath(sourcePath);
        var localTarget = GetLocalPath(targetPath);

        if (!canOverride && File.Exists(localTarget))
            throw new InvalidOperationException("Cannot move file: target file already exists.");
        if (Directory.Exists(localTarget))
            throw new InvalidOperationException("Cannot move file: target file is a directory.");
        
        File.Move(localSource, localTarget, canOverride);
        return Task.CompletedTask;
    }

    public override async Task CopyFileAsync(string sourcePath, string targetPath, bool canOverride = true, CancellationToken ct = default)
    {
        var localSource = GetLocalPath(sourcePath);
        var localTarget = GetLocalPath(targetPath);

        if (!canOverride && File.Exists(localTarget))
            throw new InvalidOperationException("Cannot copy file: target file already exists.");
        if (Directory.Exists(localTarget))
            throw new InvalidOperationException("Cannot copy file: target file is a directory.");
        
        await using var src = new FileStream(localSource, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        await using var dst = new FileStream(localTarget, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await src.CopyToAsync(dst, ct);
    }

    public override Task CreateDirAsync(string remotePath, CancellationToken ct = default)
    {
        var localPath = GetLocalPath(remotePath);
        Directory.CreateDirectory(localPath);
        return Task.CompletedTask;
    }

    public override Task DeleteDirAsync(string remotePath, CancellationToken ct = default)
    {
        var localPath = GetLocalPath(remotePath);

        if (!Directory.Exists(localPath))
        {
            throw new ArgumentException($"directory {remotePath} not exists");
        }

        Directory.Delete(localPath, recursive: true);
        return Task.CompletedTask;
    }

    public override Task RenameDirAsync(string oldName, string newName, CancellationToken ct = default)
    {
        var oldPath = GetLocalPath(oldName);
        var newPath = GetLocalPath(newName);

        Directory.Move(oldPath, newPath);
        return Task.CompletedTask;
    }

    public override Task ChangeDirectoryAsync(string path, CancellationToken ct = default)
    {
        var fullPath = GetLocalPath(path);

        if (!Directory.Exists(fullPath))
        {
            throw new ArgumentException($"directory {path} not exists");
        }

        var newCurrentPath = Path.GetRelativePath(_rootPath, fullPath);
        _currentPath = (newCurrentPath == ".") ? "" : newCurrentPath;
        return Task.CompletedTask;
    }

    public override Task<List<FileItem>> GetFilesAsync(string path, CancellationToken ct = default)
    {
        var localPath = GetLocalPath(path);
        var result = new List<FileItem>();
        foreach (var info in new DirectoryInfo(localPath).EnumerateFileSystemInfos())
        {
            ct.ThrowIfCancellationRequested();
            result.Add(new FileItem
            {
                Name = info.Name,
                Size = info is FileInfo f ? f.Length : 0,
                LastModified = info.LastWriteTime,
                IsDirectory = info is DirectoryInfo,
                FullPath = Path.GetRelativePath(_rootPath, info.FullName),
                Permissions = GetPermissionsString(info)
            });
        }
        return Task.FromResult(result);
    }

    public override Task<List<string>> GetDirectoriesAsync(string path, CancellationToken ct = default)
    {
        var localPath = GetLocalPath(path);
        var dirInfo = new DirectoryInfo(localPath);
        var result = dirInfo.GetDirectories()
            .Select(info => info.Name)
            .ToList();
        return Task.FromResult(result);
    }

    public override Task<string> GetWorkingDirectoryAsync(CancellationToken ct = default)
    {
        return Task.FromResult(_currentPath);
    }

    public override Task<bool> FileExistsAsync(string path, CancellationToken ct = default)
    {
        return Task.FromResult(File.Exists(GetLocalPath(path)));
    }

    public override Task<bool> DirExistsAsync(string path, CancellationToken ct = default)
    {
        return Task.FromResult(Directory.Exists(GetLocalPath(path)));
    }

    public override Task<FileItem> GetInfoAsync(string path, CancellationToken ct = default)
    {
        var localPath = GetLocalPath(path);
        if (Directory.Exists(localPath))
        {
            var info = new DirectoryInfo(localPath);
            return Task.FromResult(new FileItem
            {
                Name = info.Name,
                Size = 0,
                LastModified = info.LastWriteTime,
                IsDirectory = true,
                FullPath = Path.GetRelativePath(_rootPath, info.FullName),
                Permissions = GetPermissionsString(info)
            });
        }
        else
        {
            var info = new FileInfo(localPath);
            return Task.FromResult(new FileItem
            {
                Name = info.Name,
                Size = info.Length,
                LastModified = info.LastWriteTime,
                IsDirectory = false,
                FullPath = Path.GetRelativePath(_rootPath, info.FullName),
                Permissions = GetPermissionsString(info)
            });
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

    public override Task<Stream> GetFileAsync(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        string localPath = GetLocalPath(path);
        Stream stream = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        return Task.FromResult(stream);
    }

    protected override void DisposeCore()
    {
    }
}