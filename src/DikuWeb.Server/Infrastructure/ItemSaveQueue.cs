using System.Threading.Channels;
using DikuWeb.Domain.Items;
using DikuWeb.Engine;
using DikuWeb.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DikuWeb.Server.Infrastructure;

/// <summary>
/// The write side of the item persistence hand-off. The game loop calls
/// <see cref="Enqueue"/> and moves on; nothing here blocks it.
/// </summary>
public sealed class ItemSaveQueue : IItemSaveQueue
{
    private readonly Channel<ItemInstance> _channel =
        Channel.CreateUnbounded<ItemInstance>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });

    public ChannelReader<ItemInstance> Reader => _channel.Reader;

    public void Enqueue(ItemInstance item) => _channel.Writer.TryWrite(item);

    public void Complete() => _channel.Writer.TryComplete();
}

/// <summary>
/// Background worker that consumes the item save queue and persists changes to the database.
/// Coalesces multiple saves of the same item, keeping only the latest snapshot.
/// </summary>
public sealed class ItemSaveQueueWorker(
    IDbContextFactory<DikuWebDbContext> factory,
    ItemSaveQueue queue,
    ILogger<ItemSaveQueueWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var item in queue.Reader.ReadAllAsync(stoppingToken))
            {
                // Coalesce anything already queued for the same item: during shutdown or
                // a busy time the same id can appear several times, and only the last
                // matters.
                var batch = new Dictionary<Guid, ItemInstance> { [item.Id] = item };

                while (queue.Reader.TryRead(out var extra))
                {
                    batch[extra.Id] = extra;
                }

                try
                {
                    await SaveBatchAsync(batch.Values, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    ServerLog.ItemSaveQueueError(logger, ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }

        ServerLog.ItemSaveQueueStopped(logger);
    }

    private async Task SaveBatchAsync(
        IEnumerable<ItemInstance> items,
        CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        var saved = 0;
        foreach (var item in items)
        {
            // Attach and mark as modified so EF updates it (not insert)
            db.ItemInstances.Update(item);
            saved++;
        }

        await db.SaveChangesAsync(cancellationToken);
        ServerLog.ItemsSaved(logger, saved);
    }
}
