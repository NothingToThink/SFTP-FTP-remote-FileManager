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

    public override void Connect()
    {
        _isConnected = true;
    }

    public override void Disconnect()
    {
        _isConnected = false;
    }

    public override bool IsConnected => _isConnected;

    public override void SaveFile(string remotePath, Stream content)
    {
        var localPath = GetLocalPath(remotePath);

        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);

        using var fileStream = new FileStream(
            localPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            4096,
            useAsync: false);

        content.CopyTo(fileStream);
    }

    public override async Task SaveFileAsync(string remotePath, Stream content, CancellationToken ct = default)
    {
        var localPath = GetLocalPath(remotePath);
        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
        if (content.CanSeek) content.Position = 0;
        await using var fileStream = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await content.CopyToAsync(fileStream, ct);
    }

    public override void CreateFile(string remotePath)
    {
        var localPath = GetLocalPath(remotePath);

        File.Create(localPath).Dispose();
    }

    public override void DeleteFile(string remotePath)
    {
        var localPath = GetLocalPath(remotePath);

        if (!File.Exists(localPath))
        {
            throw new ArgumentException($"file {remotePath} not exists");
        }

        File.Delete(localPath);
    }

    public override void RenameFile(string oldName, string newName)
    {
        var oldPath = GetLocalPath(oldName);
        var newPath = GetLocalPath(newName);

        File.Move(oldPath, newPath);
    }

    public override void MoveFile(string sourcePath, string targetPath, bool canOverride = true)
    {
        if (!canOverride && File.Exists(targetPath))
            throw new InvalidOperationException("Cannot move file: target file already exists.");
        if (Directory.Exists(targetPath))
            throw new InvalidOperationException("Cannot move file: target file is a directory.");
        File.Move(sourcePath, targetPath);
    }

    public override void CopyFile(string sourcePath, string targetPath, bool canOverride = true)
    {
        if (!canOverride && File.Exists(targetPath))
            throw new InvalidOperationException("Cannot copy file: target file already exists.");
        if (Directory.Exists(targetPath))
            throw new InvalidOperationException("Cannot copy file: target file is a directory.");
        File.Copy(sourcePath, targetPath);
    }

    public override async Task CopyFileAsync(string sourcePath, string targetPath, bool canOverride = true, CancellationToken ct = default)
    {
        if (!canOverride && File.Exists(targetPath))
            throw new InvalidOperationException("Cannot copy file: target file already exists.");
        if (Directory.Exists(targetPath))
            throw new InvalidOperationException("Cannot copy file: target file is a directory.");
        await using var src = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        await using var dst = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await src.CopyToAsync(dst, ct);
    }

    public override void CreateDir(string remotePath)
    {
        var localPath = GetLocalPath(remotePath);

        Directory.CreateDirectory(localPath);
    }

    public override void DeleteDir(string remotePath)
    {
        var localPath = GetLocalPath(remotePath);

        if (!Directory.Exists(localPath))
        {
            throw new ArgumentException($"directory {remotePath} not exists");
        }

        Directory.Delete(localPath, recursive: true);
    }

    public override void RenameDir(string oldName, string newName)
    {
        var oldPath = GetLocalPath(oldName);
        var newPath = GetLocalPath(newName);

        Directory.Move(oldPath, newPath);
    }

    public override void ChangeDirectory(string path)
    {
        var fullPath = GetLocalPath(path);

        if (!Directory.Exists(fullPath))
        {
            throw new ArgumentException($"directory {path} not exists");
        }

        var newCurrentPath = Path.GetRelativePath(_rootPath, fullPath);

        _currentPath = (newCurrentPath == ".") ? "" : newCurrentPath;
    }

    public override List<FileItem> GetFiles(string path)
    {
        var localPath = GetLocalPath(path);

        var dirInfo = new DirectoryInfo(localPath);

        return dirInfo.GetFileSystemInfos()
            .Select(info => new FileItem
            {
                Name = info.Name,
                Size = info is FileInfo f ? f.Length : 0,
                LastModified = info.LastWriteTime,
                IsDirectory = info is DirectoryInfo,
                FullPath = Path.GetRelativePath(_rootPath, info.FullName),
                Permissions = GetPermissionsString(info)
            })
            .ToList();
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

    public override Stream GetFile(string path)
    {
        string localPath = GetLocalPath(path);

        return new FileStream(
            localPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            useAsync: false);
    }

    public override Task<Stream> GetFileAsync(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        string localPath = GetLocalPath(path);
        Stream stream = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        return Task.FromResult(stream);
    }

    public override List<string> GetDirectories(string path)
    {
        var localPath = GetLocalPath(path);

        var dirInfo = new DirectoryInfo(localPath);

        return dirInfo.GetDirectories()
            .Select(info => info.Name)
            .ToList();
    }

    public override string GetWorkingDirectory()
    {
        return _currentPath;
    }

    public override bool FileExists(string path)
    {
        return File.Exists(GetLocalPath(path));
    }
    public override bool DirExists(string path)

    {
        return Directory.Exists(GetLocalPath(path));
    }

    public override FileItem GetInfo(string path)
    {
        var localPath = GetLocalPath(path);
        if (Directory.Exists(localPath))
        {
            var info = new DirectoryInfo(path);
            return new FileItem
            {
                Name = info.Name,
                Size = 0,
                LastModified = info.LastWriteTime,
                IsDirectory = true,
                FullPath = Path.GetRelativePath(_rootPath, info.FullName),
                Permissions = GetPermissionsString(info)
            };
        }
        else
        {
            var info = new FileInfo(path);
            return new FileItem
            {
                Name = info.Name,
                Size = info.Length,
                LastModified = info.LastWriteTime,
                IsDirectory = false,
                FullPath = Path.GetRelativePath(_rootPath, info.FullName),
                Permissions = GetPermissionsString(info)
            };
        }
    }

    protected override void DisposeCore()
    {

    }
}