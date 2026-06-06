using Core.Models;

namespace Core.Interfaces.Protocol;

public abstract class Connection : IQuery, ICommand, IDisposable, IAsyncDisposable
{
    private bool _disposed = false;

    public Guid Id { get; } = Guid.NewGuid();

    public abstract bool IsConnected { get; }

    public abstract Task ConnectAsync(CancellationToken ct = default);
    public abstract Task DisconnectAsync(CancellationToken ct = default);

    public abstract Task<string> GetWorkingDirectoryAsync(CancellationToken ct = default);
    public abstract Task ChangeDirectoryAsync(string path, CancellationToken ct = default);

    public abstract Task<bool> FileExistsAsync(string path, CancellationToken ct = default);
    public abstract Task<bool> DirExistsAsync(string path, CancellationToken ct = default);
    public abstract Task<FileItem> GetInfoAsync(string path, CancellationToken ct = default);

    public abstract Task<List<FileItem>> GetFilesAsync(string path, CancellationToken ct = default);
    public abstract Task<List<string>> GetDirectoriesAsync(string path, CancellationToken ct = default);

    public abstract Task<Stream> GetFileAsync(string path, CancellationToken ct = default);
    public abstract Task SaveFileAsync(string remotePath, Stream content, CancellationToken ct = default);

    public abstract Task CreateFileAsync(string remotePath, CancellationToken ct = default);
    public abstract Task DeleteFileAsync(string remotePath, CancellationToken ct = default);
    public abstract Task RenameFileAsync(string oldName, string newName, CancellationToken ct = default);
    public abstract Task MoveFileAsync(string sourcePath, string targetPath, bool canOverride = true, CancellationToken ct = default);
    public abstract Task CopyFileAsync(string sourcePath, string targetPath, bool canOverride = true, CancellationToken ct = default);

    public abstract Task CreateDirAsync(string remotePath, CancellationToken ct = default);
    public abstract Task DeleteDirAsync(string remotePath, CancellationToken ct = default);
    public abstract Task RenameDirAsync(string oldName, string newName, CancellationToken ct = default);
    public abstract Task MoveDirAsync(string sourcePath, string targetPath, bool canOverride = true, CancellationToken ct = default);
    public abstract Task CopyDirAsync(string sourcePath, string targetPath, bool canOverride = true, CancellationToken ct = default);

    public async Task<long> GetDirectorySizeAsync(string path, CancellationToken ct = default)
    {
        long size = 0;
        foreach (FileItem item in await GetFilesAsync(path, ct))
        {
            var itemPath = Path.Combine(path, item.Name);
            if (item.IsDirectory) size += await GetDirectorySizeAsync(itemPath, ct);
            else size += item.Size;
        }
        return size;
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        DisposeCore();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await DisposeCoreAsync();
        GC.SuppressFinalize(this);
    }

    protected abstract void DisposeCore();
    protected virtual ValueTask DisposeCoreAsync() => ValueTask.CompletedTask;
}
