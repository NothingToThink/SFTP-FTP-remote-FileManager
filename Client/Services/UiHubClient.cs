using Avalonia.Threading;
using Microsoft.AspNetCore.SignalR.Client;

namespace FileManagerClient.Services;

public enum UiChannelState
{
    /// <summary>Не подключён (не запускался, остановлен или ждёт следующей попытки).</summary>
    Disconnected,
    Connecting,
    Connected,

    /// <summary>Связь потеряна, SignalR пытается восстановить её сам.</summary>
    Reconnecting,
}

/// <summary>Аргумент клиентского метода ShowMessage; severity — строка "info" | "warning" | "error".</summary>
public record ShowMessageArgs(string? Severity, string? Message, string[]? Buttons);

/// <summary>Аргумент клиентского метода ShowInputBox.</summary>
public record ShowInputBoxArgs(string? Prompt, string? Value, string? Placeholder, bool Password);

/// <summary>
/// Канал Backend → клиент (SignalR-хаб /hubs/ui): Backend вызывает ShowMessage и ShowInputBox,
/// клиент отвечает через <see cref="IDialogService"/>. Сам переподключается: сначала встроенный
/// WithAutomaticReconnect, а если соединение закрылось совсем (или первое подключение не удалось) —
/// новые попытки с нарастающей паузой до <see cref="MaxRetryDelay"/>.
/// </summary>
public sealed class UiHubClient(IDialogService dialogs) : IAsyncDisposable
{
    public static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan[] AutoReconnectDelays =
        [TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)];

    private readonly object _gate = new();

    // Диалоги Backend показывает по одному: два модальных окна на одном владельце спорят за фокус.
    private readonly SemaphoreSlim _dialogLock = new(1, 1);

    private HubConnection? _connection;
    private CancellationTokenSource? _cts;
    private string? _url;

    public UiChannelState State { get; private set; }

    /// <summary>
    /// ConnectionId текущего соединения (идёт в X-UI-Session). После переподключения он новый,
    /// поэтому читается всякий раз заново; null, пока канал не подключён.
    /// </summary>
    public string? ConnectionId => State == UiChannelState.Connected ? _connection?.ConnectionId : null;

    /// <summary>Состояние изменилось. Вызывается из любого потока.</summary>
    public event Action<UiChannelState>? StateChanged;

    /// <summary>Подключились (в том числе после переподключения). Вызывается из любого потока.</summary>
    public event Action? Connected;

    /// <summary>
    /// Запускает подключение к <paramref name="serverUrl"/> (/hubs/ui). Если уже работает с этим адресом —
    /// ничего не делает; с другим — переключается. Не ждёт подключения: попытки идут в фоне.
    /// </summary>
    public async Task StartAsync(string serverUrl)
    {
        var url = serverUrl.TrimEnd('/') + "/hubs/ui";
        lock (_gate)
        {
            if (_connection is not null && _url == url)
                return;
        }

        await StopAsync();

        var connection = new HubConnectionBuilder()
            .WithUrl(url)
            .WithAutomaticReconnect(AutoReconnectDelays)
            .Build();
        var cts = new CancellationTokenSource();
        var ct = cts.Token;

        // Обработчики — до StartAsync: иначе вызов Backend до регистрации даст ему ошибку SignalR.
        connection.On<ShowMessageArgs, string?>("ShowMessage", ShowMessageAsync);
        connection.On<ShowInputBoxArgs, string?>("ShowInputBox", ShowInputBoxAsync);

        connection.Reconnecting += _ =>
        {
            SetState(UiChannelState.Reconnecting);
            return Task.CompletedTask;
        };
        connection.Reconnected += _ =>
        {
            SetState(UiChannelState.Connected);
            Connected?.Invoke();
            return Task.CompletedTask;
        };
        connection.Closed += error =>
        {
            // Closed приходит и на наш StopAsync — тогда токен уже отменён.
            if (ct.IsCancellationRequested)
                return Task.CompletedTask;
            SetState(UiChannelState.Disconnected);
            _ = ConnectLoopAsync(connection, ct);
            return Task.CompletedTask;
        };

        lock (_gate)
        {
            _connection = connection;
            _cts = cts;
            _url = url;
        }

        _ = ConnectLoopAsync(connection, ct);
    }

    public async Task StopAsync()
    {
        HubConnection? connection;
        CancellationTokenSource? cts;
        lock (_gate)
        {
            connection = _connection;
            cts = _cts;
            _connection = null;
            _cts = null;
            _url = null;
        }

        if (connection is null)
            return;

        await cts!.CancelAsync();
        try
        {
            await connection.DisposeAsync();
        }
        catch
        {
            // остановка лучшим усилием: соединение могло уже умереть
        }

        SetState(UiChannelState.Disconnected);
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    private async Task ConnectLoopAsync(HubConnection connection, CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (!ct.IsCancellationRequested)
        {
            SetState(UiChannelState.Connecting);
            try
            {
                await connection.StartAsync(ct);
                SetState(UiChannelState.Connected);
                Connected?.Invoke();
                return;
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                SetState(UiChannelState.Disconnected);
            }

            try
            {
                await Task.Delay(delay, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxRetryDelay.Ticks));
        }
    }

    private void SetState(UiChannelState state)
    {
        if (State == state)
            return;
        State = state;
        StateChanged?.Invoke(state);
    }

    // === Обработчики вызовов Backend ===

    private async Task<string?> ShowMessageAsync(ShowMessageArgs args)
    {
        var buttons = args.Buttons is { Length: > 0 } b ? b : ["OK"];
        var severity = (args.Severity ?? string.Empty).ToLowerInvariant() switch
        {
            "warning" => DialogSeverity.Warning,
            "error" => DialogSeverity.Error,
            _ => DialogSeverity.Info,
        };

        var answer = await OnUiThreadAsync(() => dialogs.ShowMessageBoxAsync(severity, args.Message ?? string.Empty, buttons));

        // Пустой buttons: одна кнопка «OK», а ответ по контракту — null.
        return args.Buttons is { Length: > 0 } ? answer : null;
    }

    private Task<string?> ShowInputBoxAsync(ShowInputBoxArgs args)
        => OnUiThreadAsync(() => dialogs.ShowInputBoxAsync(
            args.Prompt ?? string.Empty, args.Value, args.Placeholder, args.Password));

    private async Task<string?> OnUiThreadAsync(Func<Task<string?>> show)
    {
        await _dialogLock.WaitAsync();
        try
        {
            return await Dispatcher.UIThread.InvokeAsync(show);
        }
        finally
        {
            _dialogLock.Release();
        }
    }
}
