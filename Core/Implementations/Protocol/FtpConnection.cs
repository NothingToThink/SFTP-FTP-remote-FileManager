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

    public override void Connect()
    {
        _client.Connect();
    }

    public override void Disconnect()
    {
        if (_client is { IsConnected: true })
        {
            _client.Disconnect();
            _client.Dispose();
        }
    }

    public override bool IsConnected => _client.IsConnected;

    public override List<FileItem> GetFiles(string path)
    {
        return _client.GetListing(path)
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

    public override Stream GetFile(string path)
    {
        using var ftpStream = _client.OpenRead(path);

        var memoryStream = new MemoryStream();

        ftpStream.CopyTo(memoryStream);

        memoryStream.Position = 0;

        return memoryStream;
    }

    public override List<string> GetDirectories(string path)
    {
        return _client.GetListing(path)
            .Where(f => f.Type == FtpObjectType.Directory && f.Name != "." && f.Name != "..")
            .Select(f => f.FullName)
            .ToList();
    }

    public override string GetWorkingDirectory()
    {
        return _client.GetWorkingDirectory();
    }

    public override bool FileExists(string path)
    {
        return _client.FileExists(path);
    }

    public override bool DirectoryExists(string path)
    {
        return _client.DirectoryExists(path);
    }

    public override FileItem GetInfo(string path)
    {
        var file = _client.GetObjectInfo(path);
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

    public override void SaveFile(string remotePath, Stream content)
    {
        if (content.CanSeek)
            content.Position = 0;

        _client.UploadStream(content, remotePath);
    }

    public override void CreateFile(string remotePath)
    {
        using var stream = _client.OpenWrite(remotePath);

        stream.Close();
    }

    public override void DeleteFile(string remotePath)
    {
        _client.DeleteFile(remotePath);
    }

    public override void RenameFile(string oldName, string newName)
    {
        _client.Rename(oldName, newName);
    }

    public override void MoveFile(string sourcePath, string targetPath, bool canOverride = true)
    {
        if (!canOverride && _client.FileExists(targetPath))
            throw new InvalidOperationException("Cannot move file: target file already exists.");
        if (_client.DirectoryExists(targetPath))
            throw new InvalidOperationException("Cannot move file: target file is a directory.");
        _client.Rename(sourcePath, targetPath);
    }

    public override void CopyFile(string sourcePath, string targetPath, bool canOverride = true)
    {
        if (!canOverride && _client.FileExists(targetPath))
            throw new InvalidOperationException("Cannot copy file: target file already exists.");
        if (_client.DirectoryExists(targetPath))
            throw new InvalidOperationException("Cannot copy file: target file is a directory.");

        using var ftpStream = _client.OpenRead(sourcePath);
        _client.UploadStream(ftpStream, targetPath);
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
        foreach (var entry in _client.GetListing(path))
        {
            if (entry.Name is "." or "..")
                continue;

            if (entry.Type == FtpObjectType.Directory)
                DeleteDirectoryRecursive(entry.FullName);
            else
                _client.DeleteFile(entry.FullName);
        }

        _client.DeleteDirectory(path);
    }

    public override void RenameDir(string oldName, string newName)
    {
        _client.Rename(oldName, newName);
    }

    public override void ChangeDirectory(string path)
    {
        _client.SetWorkingDirectory(path);
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