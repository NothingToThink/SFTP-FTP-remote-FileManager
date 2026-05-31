namespace Core.Interfaces.Protocol;

public interface ICommand
{
    Task SaveFileAsync(string remotePath, Stream content, CancellationToken ct = default);
    Task CreateFileAsync(string remotePath, CancellationToken ct = default);
    Task DeleteFileAsync(string remotePath, CancellationToken ct = default);
    Task RenameFileAsync(string oldName, string newName, CancellationToken ct = default);
    Task MoveFileAsync(string sourcePath, string targetPath, bool canOverride = true, CancellationToken ct = default);
    Task CopyFileAsync(string sourcePath, string targetPath, bool canOverride = true, CancellationToken ct = default);
    Task CreateDirAsync(string remotePath, CancellationToken ct = default);
    Task DeleteDirAsync(string remotePath, CancellationToken ct = default);
    Task RenameDirAsync(string oldName, string newName, CancellationToken ct = default);
    Task ChangeDirectoryAsync(string path, CancellationToken ct = default);
}