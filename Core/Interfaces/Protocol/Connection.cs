using Core.Models;

namespace Core.Interfaces.Protocol;

public abstract class Connection : IQuery, ICommand, IDisposable
{
    private bool _disposed = false;
    public Guid Id { get; } = Guid.NewGuid();
    public abstract void Connect();
    public abstract void Disconnect();
    public abstract string GetWorkingDirectory();
    public abstract bool FileExists(string path);
    public abstract bool DirExists(string path);
    public abstract FileItem GetInfo(string path);

    public abstract bool IsConnected { get; }

    public abstract List<FileItem> GetFiles(string path);
    public abstract Task<List<FileItem>> GetFilesAsync(string path, CancellationToken ct = default);
    public abstract Stream GetFile(string path);
    public abstract Task<Stream> GetFileAsync(string path, CancellationToken ct = default);
    public abstract List<string> GetDirectories(string path);
    public abstract void SaveFile(string remotePath, Stream content);
    public abstract Task SaveFileAsync(string remotePath, Stream content, CancellationToken ct = default);
    public abstract void CreateFile(string remotePath);
    public abstract void DeleteFile(string remotePath);
    public abstract void RenameFile(string oldName, string newName);
    public abstract void MoveFile(string sourcePath, string targetPath, bool canOverride = true);
    public abstract void CopyFile(string sourcePath, string targetPath, bool canOverride = true);
    public abstract Task CopyFileAsync(string sourcePath, string targetPath, bool canOverride = true, CancellationToken ct = default);
    public abstract void CreateDir(string remotePath);
    public abstract void DeleteDir(string remotePath);
    public abstract void RenameDir(string oldName, string newName);
    public abstract void ChangeDirectory(string path);

    public void Dispose()
    {
        if (!_disposed) return;
        _disposed = true;
        DisposeCore();
    }

    protected abstract void DisposeCore();
}