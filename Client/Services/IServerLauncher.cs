namespace FileManagerClient.Services;

/// <summary>
/// Управляет жизненным циклом локального сервера (Backend).
///
/// Сейчас реализован только <see cref="ExternalServerLauncher"/>: сервер запускается
/// вручную (dotnet run --project Backend), клиент лишь проверяет его доступность.
///
/// Когда появится сборка Backend, сюда добавляется BundledServerLauncher:
///   1. dotnet publish Backend -c Release -r win-x64/linux-x64 --self-contained
///      — опубликованный exe кладётся рядом с exe клиента (например, в ./server);
///   2. клиент выбирает свободный локальный порт и стартует сервер как ДОЧЕРНИЙ ПРОЦЕСС:
///      Process.Start("server/Backend", "--urls http://127.0.0.1:{port}");
///   3. готовность ждём поллингом GET /connections до таймаута;
///   4. при закрытии окна/выходе приложения — Process.Kill(entireProcessTree: true)
///      (в shutdown-пути IClassicDesktopStyleApplicationLifetime.Shutdown).
/// Благодаря этому интерфейсу MainWindowViewModel не знает, кто перед ней:
/// внешний сервер или собственный дочерний процесс.
/// </summary>
public interface IServerLauncher
{
    /// <summary>true, если процесс сервера управляется самим клиентом.</summary>
    bool IsManaged { get; }

    Task<bool> IsServerReachableAsync(CancellationToken ct = default);

    /// <summary>Гарантирует, что сервер запущен. Внешний — только проверяет, встроенный — стартует.</summary>
    Task<bool> EnsureRunningAsync(CancellationToken ct = default);

    /// <summary>Останавливает сервер, если он был запущен клиентом.</summary>
    Task ShutdownAsync();
}

/// <summary>
/// Сервер запускается снаружи (вручную / отдельно) — клиент только проверяет доступность.
/// </summary>
public class ExternalServerLauncher(Func<string> getServerUrl) : IServerLauncher
{
    public bool IsManaged => false;

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
