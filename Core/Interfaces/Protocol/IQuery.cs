using Core.Models;

namespace Core.Interfaces.Protocol;

public interface IQuery
{
    Task<List<FileItem>> GetFilesAsync(string path, CancellationToken ct = default);
    Task<Stream> GetFileAsync(string path, CancellationToken ct = default);
    Task<List<string>> GetDirectoriesAsync(string path, CancellationToken ct = default);
    Task<string> GetWorkingDirectoryAsync(CancellationToken ct = default);
    Task<bool> FileExistsAsync(string path, CancellationToken ct = default);
    Task<bool> DirExistsAsync(string path, CancellationToken ct = default);
    Task<FileItem> GetInfoAsync(string path, CancellationToken ct = default);
}
