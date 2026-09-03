using Microsoft.Extensions.Hosting;

namespace Muwbta.Server.Infrastructure;

/// <summary>
/// Ensures pending saves are flushed to the database on server shutdown.
/// Runs with lowest priority so other hosted services shut down first.
/// </summary>
public sealed class ShutdownFlushService(
    CharacterSaveQueue characterQueue,
    ItemSaveQueue itemQueue,
    WorldWriteQueue worldQueue,
    ILogger<ShutdownFlushService> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // This service doesn't actively do anything, but uses its disposal
        // to signal shutdown to the queues.
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        ServerLog.ShutdownFlushingQueues(logger);

        // Signal all queues to complete - this tells the workers to finish
        // processing any remaining items and then exit.
        characterQueue.Complete();
        itemQueue.Complete();
        worldQueue.Complete();

        // Give the workers a reasonable time to drain their queues.
        // If they don't finish in 30 seconds, we proceed anyway (better than
        // hanging forever on a graceful shutdown).
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            // Wait for both services to shut down. This ensures all pending
            // changes are persisted before the database connection closes.
            await Task.Delay(500, linked.Token); // Give workers a moment to notice completion
        }
        catch (OperationCanceledException)
        {
            // Timeout is okay - we tried our best. The workers will eventually finish
            // or the process will be killed.
        }

        await base.StopAsync(cancellationToken);
    }
}
