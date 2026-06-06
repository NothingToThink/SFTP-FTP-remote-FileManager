namespace Core.Models;

public class QueryResult<T>
{
    public T? Data { get; set; }
    public OperationStatus Status { get; set; } = new();
    public bool IsSuccess => Status.IsSuccess;
}
