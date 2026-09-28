namespace FileManagerClient.Api;

/// <summary>
/// Ошибка, полученная от Backend: код HTTP + текст из тела {"error": "..."}.
/// </summary>
public class ApiException(int statusCode, string message) : Exception($"[{statusCode}] {message}")
{
    public int StatusCode { get; } = statusCode;

    public string ServerMessage { get; } = message;
}
