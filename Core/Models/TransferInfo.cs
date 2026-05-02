namespace Core.Models;

public class TransferInfo
{
    public string FileName  { get; set; } = string.Empty;
    public long BytesTransferred { get; set; }
    public long BytesTotal { get; set; }
    //TODO
}