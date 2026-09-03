using System.Threading.Channels;
using Muwbta.Domain.Items;
using Muwbta.Engine;
using Muwbta.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Muwbta.Server.Infrastructure;

/// <summary>
/// One unit of work for the item save queue. Items are the only entity the loop creates,
/// mutates, <em>and</em> destroys at runtime, so the queue has to carry deletes rather than
/// just snapshots.
/// </summary>
public abstract record ItemSaveJob;

public sealed record SaveItemJob(ItemInstance Item) : ItemSaveJob;

/// <summary>Carries only the id: the instance is already gone from the world by this point.</summary>
public sealed record DeleteItemJob(Guid ItemId) : ItemSaveJob;

/// <summary>A caller waiting for everything enqueued before it to be durable.</summary>
public sealed record FlushItemsJob(TaskCompletionSource Completion) : ItemSaveJob;

/// <summary>
/// The write side of the item persistence hand-off. The game loop calls
/// <see cref="Enqueue"/> or <see cref="EnqueueDelete"/> and moves on; nothing here blocks it.
/// </summary>
public sealed class ItemSaveQueue : IItemSaveQueue
{
    private readonly Channel<ItemSaveJob> _channel =
        Channel.CreateUnbounded<ItemSaveJob>(new UnboundedChannelOptions
        {
            SingleReader = true,

            // Deletes are enqueued from the loop like saves are, but a flush marker comes from
            // whichever request thread is logging a player out, so writes are not single-writer.
            SingleWriter = false,
        });

    public ChannelReader<ItemSaveJob> Reader => _channel.Reader;

    public void Enqueue(ItemInstance item) => _channel.Writer.TryWrite(new SaveItemJob(item));

    public void EnqueueDelete(Guid itemId) => _channel.Writer.TryWrite(new DeleteItemJob(itemId));

    /// <summary>
    /// Completes once every job enqueued before this call has been written. Returns immediately
    /// if the queue is already closed - a caller waiting on a drained queue would wait forever.
    /// </summary>
    public Task FlushAsync(CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_channel.Writer.TryWrite(new FlushItemsJob(completion)))
        {
            return Task.CompletedTask;
        }

        return completion.Task.WaitAsync(cancellationToken);
    }

    public void Complete() => _channel.Writer.TryComplete();
}

/// <summary>
/// Drains item jobs and writes them to Postgres, entirely off the game loop thread.
/// </summary>
public sealed class ItemSaveQueueWorker(
    IDbContextFactory<MuwbtaDbContext> factory,
    ItemSaveQueue queue,
    ILogger<ItemSaveQueueWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var job in queue.Reader.ReadAllAsync(stoppingToken))
            {
                // Coalesce by item id: during a busy tick or a shutdown the same item can
                // appear several times, and only the last job for it matters. A delete
                // arriving after a save replaces it, which is what makes buy-then-sell inside
                // one window resolve to "gone" rather than "saved then orphaned".
                var batch = new Dictionary<Guid, ItemSaveJob>();
                var waiters = new List<TaskCompletionSource>();

                Absorb(job, batch, waiters);

                while (queue.Reader.TryRead(out var extra))
                {
                    Absorb(extra, batch, waiters);
                }

                try
                {
                    await WriteBatchAsync(batch.Values, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A failed write must not kill the worker, or every later save is lost too.
                    ServerLog.ItemSaveQueueError(logger, ex);
                }
                finally
                {
                    // Signal only after the batch is written, so FlushAsync means "durable".
                    // In the finally so a failed write cannot hang a player's logout.
                    foreach (var waiter in waiters)
                    {
                        waiter.TrySetResult();
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }

        // Anything still waiting would otherwise block on a queue that will never drain.
        DrainRemainingWaiters();

        ServerLog.ItemSaveQueueStopped(logger);
    }

    private static void Absorb(
        ItemSaveJob job,
        Dictionary<Guid, ItemSaveJob> batch,
        List<TaskCompletionSource> waiters)
    {
        switch (job)
        {
            case SaveItemJob save:
                batch[save.Item.Id] = save;
                break;

            case DeleteItemJob delete:
                batch[delete.ItemId] = delete;
                break;

            case FlushItemsJob flush:
                waiters.Add(flush.Completion);
                break;
        }
    }

    private void DrainRemainingWaiters()
    {
        while (queue.Reader.TryRead(out var job))
        {
            if (job is FlushItemsJob flush)
            {
                flush.Completion.TrySetResult();
            }
        }
    }

    private async Task WriteBatchAsync(
        IEnumerable<ItemSaveJob> jobs,
        CancellationToken cancellationToken)
    {
        var saves = new List<ItemInstance>();
        var deleteIds = new List<Guid>();

        foreach (var job in jobs)
        {
            switch (job)
            {
                case SaveItemJob save:
                    saves.Add(save.Item);
                    break;

                case DeleteItemJob delete:
                    deleteIds.Add(delete.ItemId);
                    break;
            }
        }

        if (saves.Count == 0 && deleteIds.Count == 0)
        {
            return;
        }

        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        if (deleteIds.Count > 0)
        {
            // Deleting by id rather than loading first: the instance is already gone from the
            // world, and an id that was never persisted simply matches no rows.
            await db.ItemInstances
                .Where(i => deleteIds.Contains(i.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        if (saves.Count > 0)
        {
            // Update() alone is wrong here. Items are created at runtime - bought, awarded by a
            // quest, conjured by a builder - so the queue is routinely handed a row that does
            // not exist yet. EF sees a set key, marks it Modified, and issues an UPDATE that
            // affects zero rows and throws, losing the whole batch with it.
            var ids = saves.Select(i => i.Id).ToList();
            var existing = await db.ItemInstances
                .AsNoTracking()
                .Where(i => ids.Contains(i.Id))
                .Select(i => i.Id)
                .ToListAsync(cancellationToken);

            var known = existing.ToHashSet();

            foreach (var item in saves)
            {
                if (known.Contains(item.Id))
                {
                    db.ItemInstances.Update(item);
                }
                else
                {
                    db.ItemInstances.Add(item);
                }
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        ServerLog.ItemsSaved(logger, saves.Count + deleteIds.Count);
    }
}
