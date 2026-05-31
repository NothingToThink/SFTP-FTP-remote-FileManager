namespace Core.Interfaces.Protocol;

public interface ICommand
{
    void SaveFile(string remotePath, Stream content);
    Task SaveFileAsync(string remotePath, Stream content, CancellationToken ct = default);
    void CreateFile(string remotePath);
    void DeleteFile(string remotePath);
    void RenameFile(string oldName, string newName);
    void MoveFile(string sourcePath, string targetPath, bool canOverride);
    void CopyFile(string sourcePath, string targetPath, bool canOverride);
    Task CopyFileAsync(string sourcePath, string targetPath, bool canOverride = true, CancellationToken ct = default);
    void CreateDir(string remotePath);
    void DeleteDir(string remotePath);
    void RenameDir(string oldName, string newName);
    void ChangeDirectory(string path);
}