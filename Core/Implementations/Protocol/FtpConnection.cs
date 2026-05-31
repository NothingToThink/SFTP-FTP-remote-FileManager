using System.Net;
using Core.Interfaces.Protocol;
using Core.Models;
using Core.Models.Credentials;
using FluentFTP;

namespace Core.Implementations.Protocol;

public class FtpConnection : Connection
{
    private readonly IAsyncFtpClient _client;

    public FtpConnection(HostProfile profile)
    {
        _client = profile.Auth switch
        {
            PasswordAuth(var user, var pwd)
                => new AsyncFtpClient(profile.Host, new NetworkCredential(user, pwd), profile.EffectivePort),

            AnonymousAuth
                => new AsyncFtpClient(profile.Host, new NetworkCredential("anonymous", "anonymous@example.com"), profile.EffectivePort),

            KeyAuth
                => throw new InvalidOperationException("Key authentication is not supported by FTP. Use SFTP instead."),

            _ => throw new ArgumentOutOfRangeException(nameof(profile.Auth))
        };
    }

    public override bool IsConnected => _client.IsConnected;

    public override void Connect() => _client.Connect().GetAwaiter().GetResult();

    public override void Disconnect()
    {
        if (_client.IsConnected)
            _client.Disconnect().GetAwaiter().GetResult();
    }

    public override List<FileItem> GetFiles(string path) => GetFilesAsync(path).GetAwaiter().GetResult();

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

    public override Stream GetFile(string path) => GetFileAsync(path).GetAwaiter().GetResult();

    public override async Task<Stream> GetFileAsync(string path, CancellationToken ct = default)
    {
        await using var ftpStream = await _client.OpenRead(path, token: ct);
        var memoryStream = new MemoryStream();
        await ftpStream.CopyToAsync(memoryStream, ct);
        memoryStream.Position = 0;
        return memoryStream;
    }

    public override List<string> GetDirectories(string path)
    {
        return _client.GetListing(path).GetAwaiter().GetResult()
            .Where(f => f.Type == FtpObjectType.Directory && f.Name != "." && f.Name != "..")
            .Select(f => f.FullName)
            .ToList();
    }

    public override string GetWorkingDirectory() => _client.GetWorkingDirectory().GetAwaiter().GetResult();

    public override bool FileExists(string path) => _client.FileExists(path).GetAwaiter().GetResult();

    public override bool DirExists(string path) => _client.DirectoryExists(path).GetAwaiter().GetResult();

    public override FileItem GetInfo(string path)
    {
        var file = _client.GetObjectInfo(path).GetAwaiter().GetResult()
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

    public override void SaveFile(string remotePath, Stream content) => SaveFileAsync(remotePath, content).GetAwaiter().GetResult();

    public override async Task SaveFileAsync(string remotePath, Stream content, CancellationToken ct = default)
    {
        if (content.CanSeek) content.Position = 0;
        await _client.UploadStream(content, remotePath, token: ct);
    }

    public override void CreateFile(string remotePath)
    {
        using var stream = _client.OpenWrite(remotePath).GetAwaiter().GetResult();
        stream.Close();
    }

    public override void DeleteFile(string remotePath) => _client.DeleteFile(remotePath).GetAwaiter().GetResult();

    public override void RenameFile(string oldName, string newName) => _client.Rename(oldName, newName).GetAwaiter().GetResult();

    public override void MoveFile(string sourcePath, string targetPath, bool canOverride = true)
    {
        if (!canOverride && _client.FileExists(targetPath).GetAwaiter().GetResult())
            throw new InvalidOperationException("Cannot move file: target file already exists.");
        if (_client.DirectoryExists(targetPath).GetAwaiter().GetResult())
            throw new InvalidOperationException("Cannot move file: target file is a directory.");
        _client.Rename(sourcePath, targetPath).GetAwaiter().GetResult();
    }

    public override void CopyFile(string sourcePath, string targetPath, bool canOverride = true) =>
        CopyFileAsync(sourcePath, targetPath, canOverride).GetAwaiter().GetResult();

    public override async Task CopyFileAsync(string sourcePath, string targetPath, bool canOverride = true, CancellationToken ct = default)
    {
        if (!canOverride && await _client.FileExists(targetPath, ct))
            throw new InvalidOperationException("Cannot copy file: target file already exists.");
        if (await _client.DirectoryExists(targetPath, ct))
            throw new InvalidOperationException("Cannot copy file: target file is a directory.");

        await using var ftpStream = await _client.OpenRead(sourcePath, token: ct);
        await _client.UploadStream(ftpStream, targetPath, token: ct);
    }

    public override void CreateDir(string remotePath) => _client.CreateDirectory(remotePath).GetAwaiter().GetResult();

    public override void DeleteDir(string remotePath) => _client.DeleteDirectory(remotePath).GetAwaiter().GetResult();

    public override void RenameDir(string oldName, string newName) => _client.Rename(oldName, newName).GetAwaiter().GetResult();

    public override void ChangeDirectory(string path) => _client.SetWorkingDirectory(path).GetAwaiter().GetResult();

    private static string GetPermissionsString(int chmod)
    {
        bool ownerRead    = (chmod & 0x100) != 0;
        bool ownerWrite   = (chmod & 0x080) != 0;
        bool ownerExecute = (chmod & 0x040) != 0;
        bool groupRead    = (chmod & 0x020) != 0;
        bool groupWrite   = (chmod & 0x010) != 0;
        bool groupExecute = (chmod & 0x008) != 0;
        bool othersRead   = (chmod & 0x004) != 0;
        bool othersWrite  = (chmod & 0x002) != 0;
        bool othersExecute= (chmod & 0x001) != 0;

        return ""
               + (ownerRead    ? "r" : "-")
               + (ownerWrite   ? "w" : "-")
               + (ownerExecute ? "x" : "-")
               + (groupRead    ? "r" : "-")
               + (groupWrite   ? "w" : "-")
               + (groupExecute ? "x" : "-")
               + (othersRead   ? "r" : "-")
               + (othersWrite  ? "w" : "-")
               + (othersExecute? "x" : "-");
    }

    protected override void DisposeCore()
    {
        _client.Dispose();
    }
}
