using System.Collections.Concurrent;
using Backend.Ui;
using FileManager.Plugins;

namespace Backend.Plugins;

/// <summary>
/// Runs plugin commands in the background, outside the HTTP request. Every run is tracked: its token is
/// cancelled when the client that started it disconnects or the Backend stops (waiting up to
/// <see cref="StopTimeout"/>), and exceptions never leave the run.
/// </summary>
public sealed class CommandRunner(IUiSessionRegistry sessions, IUiBridge bridge, ILogger<CommandRunner> logger)
    : IHostedService
{
    public static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    private readonly CancellationTokenSource _stopping = new();
    private readonly ConcurrentDictionary<Guid, Task> _running = new();

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Guid Start(PluginCommand command, CommandHandler handler, CommandContext context, string sessionId)
    {
        var runId = Guid.NewGuid();
        // RunAsync never throws; the task is kept only to be awaited on shutdown.
        _running[runId] = Task.Run(() => RunAsync(runId, command, handler, context, sessionId));
        return runId;
    }

    private async Task RunAsync(Guid runId, PluginCommand command, CommandHandler handler, CommandContext context,
        string sessionId)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);

        void OnDisconnected(string id)
        {
            if (id != sessionId)
                return;
            try
            {
                cts.Cancel();
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Cancelling command run {RunId} failed.", runId);
            }
        }

        // Subscribe before checking IsConnected: a disconnect between the two cannot be missed.
        sessions.ClientDisconnected += OnDisconnected;
        try
        {
            if (!sessions.IsConnected(sessionId))
                await cts.CancelAsync();

            UiSessionScope.Enter(sessionId);
            logger.LogInformation("Command {CommandId} started (run {RunId}).", command.Id, runId);
            await handler(context, cts.Token);
            logger.LogInformation("Command {CommandId} finished (run {RunId}).", command.Id, runId);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            logger.LogInformation("Command {CommandId} cancelled (run {RunId}).", command.Id, runId);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Command {CommandId} of plugin '{PluginId}' failed (run {RunId}).",
                command.Id, command.Plugin.Id, runId);
            await ReportAsync(command, sessionId, e);
        }
        finally
        {
            sessions.ClientDisconnected -= OnDisconnected;
            _running.TryRemove(runId, out _);
        }
    }

    private async Task ReportAsync(PluginCommand command, string sessionId, Exception error)
    {
        try
        {
            await bridge.ShowMessageAsync(sessionId, MessageSeverity.Error,
                $"{command.Plugin.DisplayName}: {error.Message}", ["OK"], _stopping.Token);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            // Backend is stopping.
        }
        catch (Exception e)
        {
            // The client is gone (or cannot show the dialog): the error is already in the log.
            logger.LogWarning(e, "Could not show the error of command {CommandId} to '{SessionId}'.",
                command.Id, sessionId);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync();
        try
        {
            await Task.WhenAll(_running.Values).WaitAsync(StopTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            logger.LogWarning("{Count} plugin command(s) did not stop within {Seconds} s.",
                _running.Count, StopTimeout.TotalSeconds);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Waiting for plugin commands was interrupted.");
        }
    }
}
