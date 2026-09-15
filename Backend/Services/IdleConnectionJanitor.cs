using Core.Interfaces.Manager;
using Core.Ssh;
using Microsoft.Extensions.Options;

namespace Backend.Services;

/// <summary>
/// Closes connections nobody has touched for a while. Without this, every abandoned browser tab
/// leaks an SSH session until the process restarts.
/// </summary>
public class IdleConnectionJanitor(
    ILogger<IdleConnectionJanitor> logger,
    IConnectionManager connectionManager,
    IOptions<SshSessionOptions> options) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var idleTimeout = options.Value.IdleTimeout;
        logger.LogInformation("Idle connection janitor started; timeout {IdleTimeout}.", idleTimeout);

        using var timer = new PeriodicTimer(SweepInterval);

        while (await SafeWaitAsync(timer, stoppingToken))
        {
            try
            {
                var collected = await connectionManager.SweepIdleAsync(idleTimeout, stoppingToken);
                if (collected > 0)
                    logger.LogInformation("Closed {Count} idle connection(s).", collected);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                // One bad connection must not kill the janitor for the rest of the process lifetime.
                logger.LogError(e, "Idle connection sweep failed.");
            }
        }
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
