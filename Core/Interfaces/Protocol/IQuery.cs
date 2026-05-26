using Core.Models;

namespace Core.Interfaces.Protocol;

public interface IQuery
{
    List<FileItem> GetFiles(string path);
    Stream GetFile(string path);
    List<string> GetDirectories(string path);
    string GetWorkingDirectory();
    bool FileExists(string path);
    bool DirectoryExists(string path);
    FileItem GetInfo(string path);
}
