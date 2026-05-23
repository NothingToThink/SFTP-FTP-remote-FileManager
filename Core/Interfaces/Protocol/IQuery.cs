using Core.Models;

namespace Core.Interfaces.Protocol;

public interface IQuery
{
    List<FileItem> GetFiles(string path);
    Stream GetFile(string path);
    List<string> GetDirectories(string path);
}