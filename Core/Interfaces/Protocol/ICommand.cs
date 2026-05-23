namespace Core.Interfaces.Protocol;

public interface ICommand
{
    void SaveFile(string remotePath, Stream content);
    void CreateFile(string remotePath);
    void DeleteFile(string remotePath);
    void RenameFile(string oldName, string newName);
    void CreateDir(string remotePath);
    void DeleteDir(string remotePath);
    void RenameDir(string oldName, string newName);
    void ChangeDirectory(string path);
    void ChangeFile(string path);
}