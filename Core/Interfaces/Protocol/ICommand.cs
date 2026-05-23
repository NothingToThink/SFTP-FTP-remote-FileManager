using Core.Models;

namespace Core.Interfaces.Protocol;

public interface ICommand
{
    OperationStatus SaveFile(string remotePath, Stream content);
    OperationStatus CreateFile(string remotePath);
    OperationStatus DeleteFile(string remotePath);
    OperationStatus RenameFile(string oldName, string newName);
    OperationStatus CreateDir(string remotePath);
    OperationStatus DeleteDir(string remotePath);
    OperationStatus RenameDir(string oldName, string newName);
    OperationStatus ChangeDirectory(string path);
    OperationStatus ChangeFile(string path);
}