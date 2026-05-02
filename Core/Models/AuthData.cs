namespace Core.Models;

public class AuthData
{
    public string Username  { get; set; } = string.Empty;
    public string? Password { get; set; }
    public string? KeyPath  { get; set; }
}