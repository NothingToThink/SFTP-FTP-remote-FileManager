using Core.Models;

namespace Core.Interfaces.UI;

public interface IFileCommandUiService
{
    Task<OperationStatus> CreateFile(string path);
    Task<OperationStatus> CreateDir(string path);
    Task<OperationStatus> DeleteFile(string path);
    Task<OperationStatus> DeleteDir(string path);
    Task<OperationStatus> Rename(string oldPath, string newPath);
}
