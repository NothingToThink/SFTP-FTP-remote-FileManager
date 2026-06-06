namespace Core.Models;

public class OperationStatus
{
    public int Code { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
}
