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
                => throw new InvalidOperationException("Anonymous authentication is not supported by SFTP."),

            _ => throw new ArgumentOutOfRangeException(nameof(profile.Auth))
        };
    }

    private static IPrivateKeySource[] BuildKeySource(string keyPath, string? passphrase)
    {
        var key = passphrase is null
            ? new PrivateKeyFile(keyPath)
            : new PrivateKeyFile(keyPath, passphrase);

        return [key];
    }

    public override async Task ConnectAsync(CancellationToken ct = default)
    {
        await _client.ConnectAsync(ct);
    }

    public override Task DisconnectAsync(CancellationToken ct = default)
    {
        if (_client.IsConnected)
        {
            _client.Disconnect();
        }

        return Task.CompletedTask;
    }

    public override Task<string> GetWorkingDirectoryAsync(CancellationToken ct = default)
    {
        return Task.FromResult(_client.WorkingDirectory);
    }

    public override Task ChangeDirectoryAsync(string path, CancellationToken ct = default)
    {
        _client.ChangeDirectory(path);
        return Task.CompletedTask;
    }

    public override async Task<bool> FileExistsAsync(string path, CancellationToken ct = default)
    {
        if (!await _client.ExistsAsync(path, ct)) return false;
        var attrs = await _client.GetAttributesAsync(path, ct);
        return attrs.IsRegularFile;
    }

    public override async Task<bool> DirExistsAsync(string path, CancellationToken ct = default)
    {
        if (!await _client.ExistsAsync(path, ct)) return false;
        var attrs = await _client.GetAttributesAsync(path, ct);
        return attrs.IsDirectory;
    }

    public override async Task<FileItem> GetInfoAsync(string path, CancellationToken ct = default)
    {
        var attrs = await _client.GetAttributesAsync(path, ct);
        return new FileItem
        {
            Name = Path.GetFileName(path),
            Size = attrs.Size,
            LastModified = attrs.LastWriteTime,
            FullPath = path,
            IsDirectory = attrs.IsDirectory,
            Permissions = GetPermissionsString(attrs),
        };
    }

    public override async Task<List<FileItem>> GetFilesAsync(string path, CancellationToken ct = default)
    {
        var files = new List<FileItem>();
        await foreach (var file in _client.ListDirectoryAsync(path, ct))
        {
            if (file.Name is "." or "..") continue;
            files.Add(new FileItem
            {
                Name = file.Name,
                Size = file.Length,
                LastModified = file.LastWriteTime,
                FullPath = file.FullName,
                IsDirectory = file.IsDirectory,
                Permissions = GetPermissionsString(file.Attributes),
            });
        }
        return files;
    }

    public override async Task<List<string>> GetDirectoriesAsync(string path, CancellationToken ct = default)
    {
        var dirs = new List<string>();
        await foreach (var file in _client.ListDirectoryAsync(path, ct))
        {
            if (file.IsDirectory && file.Name != "." && file.Name != "..")
            {
                dirs.Add(file.FullName);
            }
        }
        return dirs;
    }

    public override async Task<Stream> GetFileAsync(string path, CancellationToken ct = default)
    {
        var memoryStream = new MemoryStream();
        await _client.DownloadFileAsync(path, memoryStream, ct);
        memoryStream.Position = 0;
        return memoryStream;
    }

    public override async Task SaveFileAsync(string remotePath, Stream content, CancellationToken ct = default)
    {
        if (content.CanSeek)
            content.Position = 0;

        await _client.UploadFileAsync(content, remotePath, ct);
    }

    public override async Task CreateFileAsync(string remotePath, CancellationToken ct = default)
    {
        await _client.UploadFileAsync(Stream.Null, remotePath, ct);
    }

    public override async Task DeleteFileAsync(string remotePath, CancellationToken ct = default)
    {
        await _client.DeleteFileAsync(remotePath, ct);
    }

    public override async Task RenameFileAsync(string oldName, string newName, CancellationToken ct = default)
    {
        await _client.RenameFileAsync(oldName, newName, ct);
    }

    public override async Task MoveFileAsync(string sourcePath, string targetPath, bool canOverride = true, CancellationToken ct = default)
    {
        if (await _client.ExistsAsync(targetPath, ct))
        {
            var attrs = await _client.GetAttributesAsync(targetPath, ct);
            if (!canOverride && !attrs.IsRegularFile)
                throw new InvalidOperationException("Cannot move file: target file already exists.");

            throw new InvalidOperationException("Cannot move file: target file is a directory.");
        }
        await _client.RenameFileAsync(sourcePath, targetPath, ct);
    }

    public override async Task CopyFileAsync(string sourcePath, string targetPath, bool canOverride = true, CancellationToken ct = default)
    {
        if (await _client.ExistsAsync(targetPath, ct))
        {
            var attrs = await _client.GetAttributesAsync(targetPath, ct);
            if (!canOverride && !attrs.IsRegularFile)
                throw new InvalidOperationException("Cannot copy file: target file already exists.");

            throw new InvalidOperationException("Cannot copy file: target file is a directory.");
        }

        using var memoryStream = new MemoryStream();
        await _client.DownloadFileAsync(sourcePath, memoryStream, ct);
        memoryStream.Position = 0;
        await _client.UploadFileAsync(memoryStream, targetPath, ct);
    }

    public override async Task CreateDirAsync(string remotePath, CancellationToken ct = default)
    {
        await _client.CreateDirectoryAsync(remotePath, ct);
    }

    public override async Task DeleteDirAsync(string remotePath, CancellationToken ct = default)
    {
        await DeleteDirectoryRecursiveAsync(remotePath, ct);
    }

    private async Task DeleteDirectoryRecursiveAsync(string path, CancellationToken ct)
    {
        await foreach (var entry in _client.ListDirectoryAsync(path, ct))
        {
            if (entry.Name is "." or "..")
                continue;

            if (entry.IsDirectory)
                await DeleteDirectoryRecursiveAsync(entry.FullName, ct);
            else
                await _client.DeleteFileAsync(entry.FullName, ct);
        }

        await _client.DeleteDirectoryAsync(path, ct);
    }

    public override async Task RenameDirAsync(string oldName, string newName, CancellationToken ct = default)
    {
        await _client.RenameFileAsync(oldName, newName, ct);
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
