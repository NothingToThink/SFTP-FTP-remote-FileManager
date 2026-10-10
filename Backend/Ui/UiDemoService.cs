using System.Collections.Concurrent;

namespace Backend.Ui;

/// <summary>
/// Development-only demo: asks for a string, then shows it back. Runs the dialogs in the background
/// and keeps every started task so nothing is unobserved: failures are logged, shutdown waits for them.
/// </summary>
public class UiDemoService(IUiBridge bridge, ILogger<UiDemoService> logger) : IHostedService
{
    private readonly CancellationTokenSource _stopping = new();
    private readonly ConcurrentDictionary<Guid, Task> _running = new();

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync();
        try
        {
            // RunSafeAsync never faults, so this only waits (bounded by the host's shutdown token).
            await Task.WhenAll(_running.Values).WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Shutdown timeout elapsed: give up waiting.
        }
    }

    /// <summary>Starts the demo for the client and returns the tracked task (never faults).</summary>
    public Task Start(string sessionId)
    {
        var id = Guid.NewGuid();
        var registered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = RunSafeAsync(sessionId, id, registered.Task);
        _running[id] = task;
        registered.SetResult();
        return task;
    }

    private async Task RunSafeAsync(string sessionId, Guid id, Task registered)
    {
        await registered;
        try
        {
            var ct = _stopping.Token;
            var text = await bridge.ShowInputBoxAsync(sessionId, new InputBoxRequest("Введите строку"), ct);
            await bridge.ShowMessageAsync(sessionId, UiSeverity.Info, $"Вы ввели: {text}", ["OK"], ct);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            // Shutting down.
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "UI demo for session {SessionId} failed.", sessionId);
        }
        finally
        {
            _running.TryRemove(id, out _);
        }
    }
}
