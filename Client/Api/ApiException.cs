namespace FileManagerClient.Api;

/// <summary>
/// Ошибка, полученная от Backend: код HTTP + текст из тела {"error": "..."}.
/// Для ошибок ключа сервера (409 host_key_mismatch, 403 unknown_host_key) тело содержит
/// ещё kind/host/port/expected/presented — они разобраны в соответствующие свойства.
/// </summary>
public class ApiException(int statusCode, string message, string? kind = null, string? host = null,
    int? port = null, string? expectedFingerprint = null, string? presentedFingerprint = null)
    : Exception($"[{statusCode}] {message}")
{
    public const string KindHostKeyMismatch = "host_key_mismatch";
    public const string KindUnknownHostKey = "unknown_host_key";

    public int StatusCode { get; } = statusCode;

    public string ServerMessage { get; } = message;

    /// <summary>Машиночитаемый вид ошибки из поля kind (если бэкенд его прислал).</summary>
    public string? Kind { get; } = kind;

    public string? Host { get; } = host;

    public int? Port { get; } = port;

    /// <summary>Сохранённый (ожидаемый) отпечаток SHA256 — только при host_key_mismatch.</summary>
    public string? ExpectedFingerprint { get; } = expectedFingerprint;

    /// <summary>Отпечаток, который предъявил сервер сейчас.</summary>
    public string? PresentedFingerprint { get; } = presentedFingerprint;

    public bool IsHostKeyMismatch => Kind == KindHostKeyMismatch;

    public bool IsUnknownHostKey => Kind == KindUnknownHostKey;
}
