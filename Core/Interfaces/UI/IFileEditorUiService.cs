using Core.Models;

namespace Core.Interfaces.UI;

public interface IFileEditorUiService
{
    Task<string> OpenFile(string remotePath);
    Task<OperationStatus> SaveFile(string localTempPath, string remotePath);
}
