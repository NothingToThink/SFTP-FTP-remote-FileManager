using Core.Models;

namespace Core.Interfaces.UI;

public interface IFileQueryUiService
{
    Task<List<FileItem>> GetFiles(string path);
    Task<FileItem?> GetFileInfo(string path);
    Task<bool> Exists(string path);
}
