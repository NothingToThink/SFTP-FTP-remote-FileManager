using Core.Models;

namespace Core.Interfaces.Protocol;

public interface IQuery
{
    List<FileItem> GetFiles(string path);
    Task<List<FileItem>> GetFilesAsync(string path, CancellationToken ct = default);
    Stream GetFile(string path);
    Task<Stream> GetFileAsync(string path, CancellationToken ct = default);
    List<string> GetDirectories(string path);
    string GetWorkingDirectory();
    bool FileExists(string path);
    bool DirExists(string path);
    FileItem GetInfo(string path);
}
