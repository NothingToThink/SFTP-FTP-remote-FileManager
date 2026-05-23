using Core.Models;

namespace Core.Interfaces.Protocol;

public abstract class Connection : IQuery, ICommand, IDisposable
{
    private bool _disposed = false;
    public Guid Id { get; } = Guid.NewGuid();
    public abstract void Connect();
    public abstract void Disconnect();
    public abstract bool IsConnected { get; }
    public abstract List<FileItem> GetFiles(string path);
    public abstract Stream GetFile(string path);
    public abstract List<string> GetDirectories(string path);
    public abstract void SaveFile(string remotePath, Stream content);
    public abstract void CreateFile(string remotePath);
    public abstract void DeleteFile(string remotePath);
    public abstract void RenameFile(string oldName, string newName);
    public abstract void CreateDir(string remotePath);
    public abstract void DeleteDir(string remotePath);
    public abstract void RenameDir(string oldName, string newName);
    public abstract void ChangeDirectory(string path);
    public abstract void ChangeFile(string path);

    public void Dispose()
    {
        if (!_disposed) return;
        _disposed = true;
        DisposeCore();
    }

    protected abstract void DisposeCore();
    public abstract string GetWorkingDirectory();
}