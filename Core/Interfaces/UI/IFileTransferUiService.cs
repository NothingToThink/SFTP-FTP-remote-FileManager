using Core.Models;

namespace Core.Interfaces.UI;

public interface IFileTransferUiService
{
    Task<OperationStatus> UploadFile(string localPath, string remotePath, IProgress<double>? progress = null);
    Task<OperationStatus> DownloadFile(string remotePath, string localPath, IProgress<double>? progress = null);
    event Action<TransferInfo>? TransferProgressChanged;
}