using Core.Models;

namespace Core.Interfaces.Protocol;

public interface IQuery
{
    Task<QueryResult<List<FileItem>>> GetFiles(string path);
    Task<QueryResult<Stream>> GetFile(string path);
    Task<QueryResult<List<string>>> GetDirectories(string path);
    Task<QueryResult<string>> GetWorkingDirectory();
}