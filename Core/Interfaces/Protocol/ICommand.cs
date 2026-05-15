using Core.Models;

namespace Core.Interfaces.Protocol;

public interface ICommand
{
    Task<OperationStatus> SaveFile(string remotePath, Stream content);
    Task<OperationStatus> CreateFile(string remotePath);
    Task<OperationStatus> DeleteFile(string remotePath);
    Task<OperationStatus> RenameFile(string oldName, string newName);
    Task<OperationStatus> CreateDir(string remotePath);
    Task<OperationStatus> DeleteDir(string remotePath);
    Task<OperationStatus> RenameDir(string oldName, string newName);
    Task<OperationStatus> ChangeDirectory(string path);
    Task<OperationStatus> ChangeFile(string path);
}