using Core.Interfaces.Protocol;
using Core.Models;
using Core.Ssh;
using Renci.SshNet.Sftp;

namespace Core.Implementations.Protocol;

/// <summary>
/// SFTP file operations on top of a shared <see cref="ISshSession"/>. The session owns the
/// transport, host key verification and lifetime; this class only speaks the file protocol.
/// </summary>
public class SftpConnection(ISshSession session) : Connection, ISshSessionProvider
{
    public ISshSession Session { get; } = session ?? throw new ArgumentNullException(nameof(session));

    public override bool IsConnected => Session.IsConnected;

    public override Task ConnectAsync(CancellationToken ct = default)
        => Session.ConnectAsync(ct);

    public override Task DisconnectAsync(CancellationToken ct = default)
        => Session.DisconnectAsync(ct);

    public override async Task<string> GetWorkingDirectoryAsync(CancellationToken ct = default)
    {
        var client = await Session.GetSftpAsync(ct);
        return client.WorkingDirectory;
    }

    public override async Task ChangeDirectoryAsync(string path, CancellationToken ct = default)
    {
        var client = await Session.GetSftpAsync(ct);
        client.ChangeDirectory(path);
    }

    public override async Task<bool> FileExistsAsync(string path, CancellationToken ct = default)
    {
        var client = await Session.GetSftpAsync(ct);
        if (!await client.ExistsAsync(path, ct)) return false;
        var attrs = await client.GetAttributesAsync(path, ct);
        return attrs.IsRegularFile;
    }

    public override async Task<bool> DirExistsAsync(string path, CancellationToken ct = default)
    {
        var client = await Session.GetSftpAsync(ct);
        if (!await client.ExistsAsync(path, ct)) return false;
        var attrs = await client.GetAttributesAsync(path, ct);
        return attrs.IsDirectory;
    }

    public override async Task<FileItem> GetInfoAsync(string path, CancellationToken ct = default)
    {
        var client = await Session.GetSftpAsync(ct);
        var attrs = await client.GetAttributesAsync(path, ct);
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
        var client = await Session.GetSftpAsync(ct);
        var files = new List<FileItem>();
        await foreach (var file in client.ListDirectoryAsync(path, ct))
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
        var client = await Session.GetSftpAsync(ct);
        var dirs = new List<string>();
        await foreach (var file in client.ListDirectoryAsync(path, ct))
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
        var client = await Session.GetSftpAsync(ct);
        var memoryStream = new MemoryStream();
        await client.DownloadFileAsync(path, memoryStream, ct);
        memoryStream.Position = 0;
        return memoryStream;
    }

    public override async Task SaveFileAsync(string remotePath, Stream content, CancellationToken ct = default)
    {
        var client = await Session.GetSftpAsync(ct);
        if (content.CanSeek)
            content.Position = 0;

        await client.UploadFileAsync(content, remotePath, ct);
    }

    public override async Task CreateFileAsync(string remotePath, CancellationToken ct = default)
    {
        var client = await Session.GetSftpAsync(ct);
        await client.UploadFileAsync(Stream.Null, remotePath, ct);
    }

    public override async Task DeleteFileAsync(string remotePath, CancellationToken ct = default)
    {
        var client = await Session.GetSftpAsync(ct);
        await client.DeleteFileAsync(remotePath, ct);
    }

    public override async Task RenameFileAsync(string oldName, string newName, CancellationToken ct = default)
    {
        var client = await Session.GetSftpAsync(ct);
        await client.RenameFileAsync(oldName, newName, ct);
    }

    public override async Task MoveFileAsync(string sourcePath, string targetPath, bool canOverride = true, CancellationToken ct = default)
    {
        var client = await Session.GetSftpAsync(ct);
        await EnsureTargetWritableAsync(client, targetPath, canOverride, "move", ct);
        await client.RenameFileAsync(sourcePath, targetPath, ct);
    }

    public override async Task CopyFileAsync(string sourcePath, string targetPath, bool canOverride = true, CancellationToken ct = default)
    {
        var client = await Session.GetSftpAsync(ct);
        await EnsureTargetWritableAsync(client, targetPath, canOverride, "copy", ct);

        using var memoryStream = new MemoryStream();
        await client.DownloadFileAsync(sourcePath, memoryStream, ct);
        memoryStream.Position = 0;
        await client.UploadFileAsync(memoryStream, targetPath, ct);
    }

    public override async Task CreateDirAsync(string remotePath, CancellationToken ct = default)
    {
        var client = await Session.GetSftpAsync(ct);
        await client.CreateDirectoryAsync(remotePath, ct);
    }

    public override async Task DeleteDirAsync(string remotePath, CancellationToken ct = default)
    {
        var client = await Session.GetSftpAsync(ct);
        await DeleteDirectoryRecursiveAsync(client, remotePath, ct);
    }

    public override async Task RenameDirAsync(string oldName, string newName, CancellationToken ct = default)
    {
        var client = await Session.GetSftpAsync(ct);
        await client.RenameFileAsync(oldName, newName, ct);
    }

    /// <summary>
    /// Rejects a target that cannot be written: a directory never can, and an existing file only
    /// when overwriting was allowed.
    /// </summary>
    private static async Task EnsureTargetWritableAsync(
        Renci.SshNet.SftpClient client,
        string targetPath,
        bool canOverride,
        string operation,
        CancellationToken ct)
    {
        if (!await client.ExistsAsync(targetPath, ct))
            return;

        var attrs = await client.GetAttributesAsync(targetPath, ct);

        if (attrs.IsDirectory)
            throw new InvalidOperationException($"Cannot {operation} file: target is a directory.");

        if (!canOverride)
            throw new InvalidOperationException($"Cannot {operation} file: target file already exists.");
    }

    private static async Task DeleteDirectoryRecursiveAsync(
        Renci.SshNet.SftpClient client,
        string path,
        CancellationToken ct)
    {
        await foreach (var entry in client.ListDirectoryAsync(path, ct))
        {
            if (entry.Name is "." or "..")
                continue;

            if (entry.IsDirectory)
                await DeleteDirectoryRecursiveAsync(client, entry.FullName, ct);
            else
                await client.DeleteFileAsync(entry.FullName, ct);
        }

        await client.DeleteDirectoryAsync(path, ct);
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
        // The session owns the transport; disposing it is what actually closes the sockets.
        Session.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    protected override ValueTask DisposeCoreAsync() => Session.DisposeAsync();
}
