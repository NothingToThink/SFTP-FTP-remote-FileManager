namespace FileManagerClient.Services;

/// <summary>
/// Управляет жизненным циклом локального сервера (Backend).
///
/// <see cref="ExternalServerLauncher"/> — сервер запускают снаружи (dotnet run --project Backend),
/// клиент лишь проверяет доступность.
/// <see cref="BundledServerLauncher"/> — сервер лежит рядом с клиентом (./server/Backend) либо,
/// в dev-репозитории, стартует через dotnet run; клиент выбирает свободный порт на 127.0.0.1,
/// запускает его дочерним процессом без консоли, пишет лог в папку данных, ждёт готовности
/// (GET /connections) и убивает всё дерево процессов при выходе.
/// MainWindowViewModel не знает, кто перед ней: внешний сервер или собственный дочерний процесс.
/// </summary>
public interface IServerLauncher
{
    /// <summary>true, если процесс сервера управляется самим клиентом.</summary>
    bool IsManaged { get; }

    /// <summary>Адрес, по которому API-клиент должен ходить (после StartAsync — адрес запущенного сервера).</summary>
    string ServerUrl { get; }

    /// <summary>Запускает сервер (если управляемый) и возвращает результат с человеческим описанием ошибки.</summary>
    Task<ServerStartResult> StartAsync(CancellationToken ct = default);

    Task<bool> IsServerReachableAsync(CancellationToken ct = default);

    /// <summary>Гарантирует, что сервер запущен. Внешний — только проверяет, встроенный — стартует.</summary>
    Task<bool> EnsureRunningAsync(CancellationToken ct = default);

    /// <summary>Останавливает сервер, если он был запущен клиентом.</summary>
    Task ShutdownAsync();
}

/// <summary>Результат запуска: при неудаче — заголовок, понятный текст и технические подробности (хвост лога).</summary>
public record ServerStartResult(bool Ok, string? Title = null, string? Message = null, string? Details = null)
{
    public static ServerStartResult Success { get; } = new(true);
}

/// <summary>
/// Сервер запускается снаружи (вручную / отдельно) — клиент только проверяет доступность.
/// </summary>
public class ExternalServerLauncher(Func<string> getServerUrl) : IServerLauncher
{
    public bool IsManaged => false;

    public string ServerUrl => getServerUrl();

    public async Task<ServerStartResult> StartAsync(CancellationToken ct = default)
        => await IsServerReachableAsync(ct)
            ? ServerStartResult.Success
            : new ServerStartResult(false, "Сервер не отвечает",
                $"Сервер {ServerUrl} не отвечает. Запустите его вручную или проверьте адрес в настройках.");

    public async Task<bool> IsServerReachableAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var resp = await http.GetAsync($"{getServerUrl().TrimEnd('/')}/connections", ct);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public Task<bool> EnsureRunningAsync(CancellationToken ct = default)
        => IsServerReachableAsync(ct);

    public Task ShutdownAsync() => Task.CompletedTask;
}
