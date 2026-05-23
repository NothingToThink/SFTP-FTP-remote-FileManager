using Core.Models;

namespace Core.Interfaces.Protocol;

public interface IQuery
{
    QueryResult<List<FileItem>> GetFiles(string path);
    QueryResult<Stream> GetFile(string path);
    QueryResult<List<string>> GetDirectories(string path);
}