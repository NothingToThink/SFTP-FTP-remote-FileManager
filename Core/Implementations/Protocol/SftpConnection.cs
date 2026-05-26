using Core.Interfaces.Protocol;
using Core.Models;
using Core.Models.Credentials;
using Renci.SshNet;
using Renci.SshNet.Sftp;

namespace Core.Implementations.Protocol;

public class SftpConnection : Connection
{
    private readonly SftpClient _client;

    public override bool IsConnected => _client.IsConnected;

    public SftpConnection(HostProfile profile)
    {
        _client = profile.Auth switch
        {
            PasswordAuth(var user, var pwd)
                => new SftpClient(profile.Host, profile.EffectivePort, user, pwd),

            KeyAuth(var user, var keyPath, var passphrase)
                => new SftpClient(profile.Host, profile.EffectivePort, user, BuildKeySource(keyPath, passphrase)),

            AnonymousAuth
                => throw new InvalidOperationException("Anonymous authentication doesn't supported by sftp authentication"),

            _ => throw new ArgumentOutOfRangeException(nameof(profile.Auth))
        };
    }

    public override void Connect()
    {
        _client.Connect();
    }

    private static IPrivateKeySource[] BuildKeySource(string keyPath, string? passphrase)
    {
        var key = passphrase is null
            ? new PrivateKeyFile(keyPath)
            : new PrivateKeyFile(keyPath, passphrase);

        return [key];
    }

    public override void Disconnect()
    {
        if (_client is { IsConnected: true })
        {
            _client.Disconnect();
        }
    }

    public override List<FileItem> GetFiles(string path)
    {
        return _client.ListDirectory(path)
            .Where(f => f.Name != "." && f.Name != "..")
            .Select(file => new FileItem
            {
                Name = file.Name,
                Size = file.Length,
                LastModified = file.LastWriteTime,
                FullPath = file.FullName,
                IsDirectory = file.IsDirectory,
                Permissions = GetPermissionsString(file.Attributes),
            })
            .ToList();
    }

    public override Stream GetFile(string path)
    {
        var memoryStream = new MemoryStream();

        _client.DownloadFile(path, memoryStream);

        memoryStream.Position = 0;

        return memoryStream;
    }

    public override List<string> GetDirectories(string path)
    {
        return _client.ListDirectory(path)
            .Where(f => f.IsDirectory && f.Name != "." && f.Name != "..")
            .Select(f => f.FullName)
            .ToList();
    }

    public override string GetWorkingDirectory()
    {
        return _client.WorkingDirectory;
    }

    public override bool FileExists(string path)
    {
        if (!_client.Exists(path)) return false;
        return _client.GetAttributes(path).IsRegularFile;
    }

    public override bool DirectoryExists(string path)
    {
        if (!_client.Exists(path)) return false;
        return _client.GetAttributes(path).IsDirectory;
    }

    public override FileItem GetInfo(string path)
    {
        var file = _client.Get(path);
        return new FileItem
        {
            Name = file.Name,
            Size = file.Length,
            LastModified = file.LastWriteTime,
            FullPath = file.FullName,
            IsDirectory = file.IsDirectory,
            Permissions = GetPermissionsString(file.Attributes),
        };
    }

    public override void SaveFile(string remotePath, Stream content)
    {
        if (content.CanSeek)
            content.Position = 0;

        _client.UploadFile(content, remotePath, true);
    }

    public override void CreateFile(string remotePath)
    {
        using var stream = _client.Create(remotePath);
    }

    public override void DeleteFile(string remotePath)
    {
        _client.DeleteFile(remotePath);
    }

    public override void RenameFile(string oldName, string newName)
    {
        _client.RenameFile(oldName, newName);
    }

    public override void MoveFile(string sourcePath, string targetPath, bool canOverride = true)
    {
        if (_client.Exists(targetPath))
        {
            if (!canOverride && !_client.GetAttributes(targetPath).IsRegularFile)
                throw new InvalidOperationException("Cannot move file: target file already exists.");
            else
                throw new InvalidOperationException("Cannot move file: target file is a directory.");
        }
        _client.RenameFile(sourcePath, targetPath);
    }

    public override void CopyFile(string sourcePath, string targetPath, bool canOverride = true)
    {
        if (_client.Exists(targetPath))
        {
            if (!canOverride && !_client.GetAttributes(targetPath).IsRegularFile)
                throw new InvalidOperationException("Cannot copy file: target file already exists.");
            else
                throw new InvalidOperationException("Cannot copy file: target file is a directory.");
        }        
        using var memoryStream = new MemoryStream();
        _client.DownloadFile(sourcePath, memoryStream);
        _client.UploadFile(memoryStream, targetPath);
    }

    public override void CreateDir(string remotePath)
    {
        _client.CreateDirectory(remotePath);
    }

    public override void DeleteDir(string remotePath)
    {
        DeleteDirectoryRecursive(remotePath);
    }

    private void DeleteDirectoryRecursive(string path)
    {
        foreach (var entry in _client.ListDirectory(path))
        {
            if (entry.Name is "." or "..")
                continue;

            if (entry.IsDirectory)
                DeleteDirectoryRecursive(entry.FullName);
            else
                _client.DeleteFile(entry.FullName);
        }

        _client.DeleteDirectory(path);
    }

    public override void RenameDir(string oldName, string newName)
    {
        _client.RenameFile(oldName, newName);
    }

    public override void ChangeDirectory(string path)
    {
        _client.ChangeDirectory(path);
    }

    public override void ChangeFile(string path)
    {
        if (!_client.Exists(path))
        {
            throw new FileNotFoundException("File does not exist", path);
        }

        var attrs = _client.GetAttributes(path);

        if (attrs.IsDirectory)
        {
            throw new InvalidOperationException("Path is a directory, not a file");
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

    protected override void DisposeCore()
    {
        _client.Dispose();
    }
}