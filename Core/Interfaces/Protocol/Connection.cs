using Core.Models;
using Core.Models.Credentials;

namespace Core.Interfaces.Protocol;

public abstract class Connection : IQuery, ICommand, IDisposable
{
    private bool _disposed = false;
    public Guid Id { get; } = Guid.NewGuid();
    public abstract OperationStatus Connect();
    public abstract OperationStatus Disconnect();
    public abstract bool IsConnected { get; }
    public abstract QueryResult<List<FileItem>> GetFiles(string path);
    public abstract QueryResult<Stream> GetFile(string path);
    public abstract QueryResult<List<string>> GetDirectories(string path);
    public abstract OperationStatus SaveFile(string remotePath, Stream content);
    public abstract OperationStatus CreateFile(string remotePath);
    public abstract OperationStatus DeleteFile(string remotePath);
    public abstract OperationStatus RenameFile(string oldName, string newName);
    public abstract OperationStatus CreateDir(string remotePath);
    public abstract OperationStatus DeleteDir(string remotePath);
    public abstract OperationStatus RenameDir(string oldName, string newName);
    public abstract OperationStatus ChangeDirectory(string path);
    public abstract OperationStatus ChangeFile(string path);

    public void Dispose()
    {
        if (!_disposed) return;
        _disposed = true;
        DisposeCore();
    }

    protected abstract void DisposeCore();
    public abstract QueryResult<string> GetWorkingDirectory();
}