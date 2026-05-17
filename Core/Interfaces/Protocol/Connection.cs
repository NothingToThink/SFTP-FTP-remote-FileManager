using Core.Models;
using Core.Models.Credentials;

namespace Core.Interfaces.Protocol;

public abstract class Connection : IQuery, ICommand, IDisposable
{
    private bool _disposed = false;
    public Guid Id { get; } = Guid.NewGuid();
    public abstract Task<OperationStatus> Connect();
    public abstract Task<OperationStatus> Disconnect();
    public abstract bool IsConnected { get; }
    public abstract Task<QueryResult<List<FileItem>>> GetFiles(string path);
    public abstract Task<QueryResult<Stream>> GetFile(string path);
    public abstract Task<QueryResult<List<string>>> GetDirectories(string path);
    public abstract Task<OperationStatus> SaveFile(string remotePath, Stream content);
    public abstract Task<OperationStatus> CreateFile(string remotePath);
    public abstract Task<OperationStatus> DeleteFile(string remotePath);
    public abstract Task<OperationStatus> RenameFile(string oldName, string newName);
    public abstract Task<OperationStatus> CreateDir(string remotePath);
    public abstract Task<OperationStatus> DeleteDir(string remotePath);
    public abstract Task<OperationStatus> RenameDir(string oldName, string newName);
    public abstract Task<OperationStatus> ChangeDirectory(string path);
    public abstract Task<OperationStatus> ChangeFile(string path);

    public void Dispose()
    {
        if (!_disposed) return;
        _disposed = true;
        DisposeCore();
    }

    protected abstract void DisposeCore();
    public abstract Task<QueryResult<string>> GetWorkingDirectory();
}