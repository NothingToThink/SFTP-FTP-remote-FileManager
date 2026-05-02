using Core.Models;

namespace Core.Interfaces.Protocol;

public interface IQuery
{
    Task<List<FileItem>> GetFiles(string path);
    Task<FileItem> GetFile(string path);
    Task<List<string>> GetDirectories(string path);
    Task<OperationStatus> ChangeDirectory(string path);
    Task<OperationStatus> ChangeFile(string path);
}