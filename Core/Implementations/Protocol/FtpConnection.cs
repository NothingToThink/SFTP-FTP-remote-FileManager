using System.Net;
using Core.Interfaces.Protocol;
using Core.Models;
using Core.Models.Credentials;
using FluentFTP;

namespace Core.Implementations.Protocol;

public class FtpConnection : Connection
{
    private readonly HostProfile _profile;
    private readonly IAsyncFtpClient _client;

    public FtpConnection(HostProfile profile)
    {
        _profile = profile;
        _client = CreateClient();
    }

    /// <summary>
    /// Builds a new, not yet connected client. Used for the shared control client and for dedicated
    /// clients that carry a single long transfer.
    /// </summary>
    private AsyncFtpClient CreateClient()
    {
        var client = _profile.Auth switch
        {
            PasswordAuth(var user, var pwd)
                => new AsyncFtpClient(_profile.Host, new NetworkCredential(user, pwd), _profile.EffectivePort),

            AnonymousAuth
                => new AsyncFtpClient(_profile.Host, new NetworkCredential("anonymous", "anonymous@example.com"), _profile.EffectivePort),

            KeyAuth
                => throw new InvalidOperationException("Key authentication is not supported by FTP. Use SFTP instead."),

            _ => throw new ArgumentOutOfRangeException(nameof(_profile.Auth))
        };

        // Large files: keep sockets alive and do not drop slow transfers.
        client.Config.SocketKeepAlive = true;
        client.Config.DataConnectionReadTimeout = 5 * 60 * 1000;
        client.Config.ReadTimeout = 60 * 1000;
        client.Config.TransferChunkSize = 256 * 1024;
        client.Config.LocalFileBufferSize = 256 * 1024;
        return client;
    }

    private async Task<AsyncFtpClient> CreateConnectedClientAsync(CancellationToken ct)
    {
        var client = CreateClient();
        try
        {
            await client.Connect(ct);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public override bool IsConnected => _client.IsConnected;

    public override async Task ConnectAsync(CancellationToken ct = default)
    {
        await _client.Connect(ct);
    }

    public override async Task DisconnectAsync(CancellationToken ct = default)
    {
        if (_client.IsConnected)
        {
            await _client.Disconnect(ct);
        }
    }

    public override async Task<List<FileItem>> GetFilesAsync(string path, CancellationToken ct = default)
    {
        var listing = await _client.GetListing(path, token: ct);
        return listing
            .Where(f => f.Name != "." && f.Name != "..")
            .Select(file => new FileItem
            {
                Name = file.Name,
                Size = file.Size,
                LastModified = file.Modified,
                FullPath = file.FullName,
                IsDirectory = file.Type == FtpObjectType.Directory,
                Permissions = GetPermissionsString(file.Chmod),
            })
            .ToList();
    }

    public override async Task<Stream> GetFileAsync(string path, CancellationToken ct = default)
    {
        return await _client.OpenRead(path, token: ct);
    }

    public override async Task<List<string>> GetDirectoriesAsync(string path, CancellationToken ct = default)
    {
        var listing = await _client.GetListing(path, token: ct);
        return listing
            .Where(f => f.Type == FtpObjectType.Directory && f.Name != "." && f.Name != "..")
            .Select(f => f.FullName)
            .ToList();
    }

    public override async Task<string> GetWorkingDirectoryAsync(CancellationToken ct = default)
    {
        return await _client.GetWorkingDirectory(ct);
    }

    public override async Task<bool> FileExistsAsync(string path, CancellationToken ct = default)
    {
        return await _client.FileExists(path, ct);
    }

    public override async Task<bool> DirExistsAsync(string path, CancellationToken ct = default)
    {
        return await _client.DirectoryExists(path, ct);
    }

    public override async Task<FileItem> GetInfoAsync(string path, CancellationToken ct = default)
    {
        var file = await _client.GetObjectInfo(path, token: ct)
            ?? throw new FileNotFoundException($"Path not found: {path}");
        return new FileItem
        {
            Name = file.Name,
            Size = file.Size,
            LastModified = file.Modified,
            FullPath = file.FullName,
            IsDirectory = file.Type == FtpObjectType.Directory,
            Permissions = GetPermissionsString(file.Chmod),
        };
    }

    public override async Task SaveFileAsync(string remotePath, Stream content, CancellationToken ct = default)
    {
        if (content.CanSeek) content.Position = 0;
        using var client = await CreateConnectedClientAsync(ct);
        await client.UploadStream(content, remotePath, token: ct);
    }

    public override async Task CreateFileAsync(string remotePath, CancellationToken ct = default)
    {
        await using var stream = await _client.OpenWrite(remotePath, token: ct);
    }

    public override async Task DeleteFileAsync(string remotePath, CancellationToken ct = default)
    {
        await _client.DeleteFile(remotePath, ct);
    }

    public override async Task RenameFileAsync(string oldName, string newName, CancellationToken ct = default)
    {
        await _client.Rename(oldName, newName, ct);
    }

    public override async Task MoveFileAsync(string sourcePath, string targetPath, bool canOverride = true, CancellationToken ct = default)
    {
        if (!canOverride && await _client.FileExists(targetPath, ct))
            throw new InvalidOperationException("Cannot move file: target file already exists.");
        if (await _client.DirectoryExists(targetPath, ct))
            throw new InvalidOperationException("Cannot move file: target file is a directory.");
        await _client.Rename(sourcePath, targetPath, ct);
    }

    public override async Task CopyFileAsync(string sourcePath, string targetPath, bool canOverride = true, CancellationToken ct = default)
    {
        if (!canOverride && await _client.FileExists(targetPath, ct))
            throw new InvalidOperationException("Cannot copy file: target file already exists.");
        if (await _client.DirectoryExists(targetPath, ct))
            throw new InvalidOperationException("Cannot copy file: target file is a directory.");

        // One FTP control connection cannot read and write at the same time, so reading and
        // writing use two separate clients.
        using var reader = await CreateConnectedClientAsync(ct);
        using var writer = await CreateConnectedClientAsync(ct);
        await using var ftpStream = await reader.OpenRead(sourcePath, token: ct);
        await writer.UploadStream(ftpStream, targetPath, token: ct);
    }

    public override async Task CreateDirAsync(string remotePath, CancellationToken ct = default)
    {
        await _client.CreateDirectory(remotePath, token: ct);
    }

    public override async Task DeleteDirAsync(string remotePath, CancellationToken ct = default)
    {
        await _client.DeleteDirectory(remotePath, token: ct);
    }

    public override async Task RenameDirAsync(string oldName, string newName, CancellationToken ct = default)
    {
        await _client.Rename(oldName, newName, ct);
    }

    public override async Task MoveDirAsync(string sourcePath, string targetPath, bool canOverride = true, CancellationToken ct = default)
    {
        if (!canOverride && await _client.DirectoryExists(targetPath, ct))
            throw new InvalidOperationException("Cannot move directory: target directory already exists.");
        if (await _client.FileExists(targetPath, ct))
            throw new InvalidOperationException("Cannot move directory: target directory is a file.");
        await _client.MoveDirectory(sourcePath, targetPath, token: ct);
    }

    public override async Task CopyDirAsync(string sourcePath, string targetPath, bool canOverride = true, CancellationToken ct = default)
    {
        if (!canOverride)
        {
            if (await _client.DirectoryExists(targetPath, ct))
                throw new InvalidOperationException("Cannot copy directory: target directory already exists.");
            else
                await _client.CreateDirectory(targetPath, token: ct);
        }
        if (await _client.FileExists(targetPath, ct))
            throw new InvalidOperationException("Cannot copy directory: target directory is a file.");


        foreach (var item in await GetFilesAsync(sourcePath, ct))
        {
            var sourceItemPath = Path.Combine(sourcePath, item.Name);
            var targetItemPath = Path.Combine(targetPath, item.Name);

            if (item.IsDirectory)
                await CopyDirAsync(sourceItemPath, targetItemPath, canOverride, ct);
            else
                await CopyFileAsync(sourceItemPath, targetItemPath, canOverride, ct);
        }
    }

    public override async Task ChangeDirectoryAsync(string path, CancellationToken ct = default)
    {
        await _client.SetWorkingDirectory(path, ct);
    }

    private static string GetPermissionsString(int chmod)
    {
        bool ownerRead = (chmod & 0x100) != 0;
        bool ownerWrite = (chmod & 0x080) != 0;
        bool ownerExecute = (chmod & 0x040) != 0;
        bool groupRead = (chmod & 0x020) != 0;
        bool groupWrite = (chmod & 0x010) != 0;
        bool groupExecute = (chmod & 0x008) != 0;
        bool othersRead = (chmod & 0x004) != 0;
        bool othersWrite = (chmod & 0x002) != 0;
        bool othersExecute = (chmod & 0x001) != 0;

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
