namespace FileManager.Plugins;

public interface IFileSystem
{
    Task<IReadOnlyList<FileEntry>> ListAsync(Guid connectionId, string path, CancellationToken ct = default);
    Task<FileEntry> StatAsync(Guid connectionId, string path, CancellationToken ct = default);
    Task<Stream> OpenReadAsync(Guid connectionId, string path, CancellationToken ct = default);
    Task WriteAsync(Guid connectionId, string path, Stream content, bool overwrite, CancellationToken ct = default);
    Task CreateDirectoryAsync(Guid connectionId, string path, CancellationToken ct = default);
    Task DeleteAsync(Guid connectionId, string path, bool recursive, CancellationToken ct = default);
    Task MoveAsync(Guid connectionId, string from, string to, bool overwrite, CancellationToken ct = default);
    Task CopyAsync(Guid connectionId, string from, string to, bool overwrite, CancellationToken ct = default);
}

public sealed record FileEntry(
    string Name,
    string FullPath,
    bool IsDirectory,
    long Size,
    DateTimeOffset LastModified,
    string? Permissions);
