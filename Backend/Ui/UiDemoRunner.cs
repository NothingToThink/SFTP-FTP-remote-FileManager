using System.Collections.Concurrent;

namespace Backend.Ui;

/// <summary>
/// Runs the demo dialogs in the background for POST /dev/ui/demo. Every started task is tracked: its
/// exceptions are logged, and on shutdown the runner cancels and awaits all of them.
/// </summary>
public class UiDemoRunner(IUiBridge bridge, ILogger<UiDemoRunner> logger) : IAsyncDisposable
{
    private readonly CancellationTokenSource _stopping = new();
    private readonly ConcurrentDictionary<Task, byte> _running = new();

    public void Start(string sessionId)
    {
        // RunAsync never throws, so finished tasks can simply be dropped.
        foreach (var finished in _running.Keys.Where(t => t.IsCompleted))
            _running.TryRemove(finished, out _);

        _running[RunAsync(sessionId, _stopping.Token)] = 0;
    }

    private async Task RunAsync(string sessionId, CancellationToken ct)
    {
        try
        {
            var input = await bridge.ShowInputBoxAsync(sessionId, new InputBoxRequest("Введите строку"), ct);
            if (input is null)
            {
                logger.LogInformation("UI demo for {SessionId}: input dismissed.", sessionId);
                return;
            }

            await bridge.ShowMessageAsync(sessionId, MessageSeverity.Info, $"Вы ввели: {input}", ["OK"], ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Backend is shutting down.
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "UI demo for {SessionId} failed.", sessionId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        await Task.WhenAll(_running.Keys);
        _stopping.Dispose();
    }
}
