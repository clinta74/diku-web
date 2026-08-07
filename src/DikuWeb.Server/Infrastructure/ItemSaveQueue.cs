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
    private readonly Channel<(ItemInstance?, TaskCompletionSource?)> _channel =
        Channel.CreateUnbounded<(ItemInstance?, TaskCompletionSource?)>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });

    public ChannelReader<(ItemInstance?, TaskCompletionSource?)> Reader => _channel.Reader;

    public void Enqueue(ItemInstance item) => _channel.Writer.TryWrite((item, null));

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource();
        if (!_channel.Writer.TryWrite((null, tcs)))
        {
            tcs.SetException(new InvalidOperationException("Item save queue is closed"));
        }
        await tcs.Task.ConfigureAwait(false);
    }

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
            await foreach (var (item, flushMarker) in queue.Reader.ReadAllAsync(stoppingToken))
            {
                if (flushMarker is not null)
                {
                    // Flush marker: drain any pending saves then complete the marker
                    try
                    {
                        await SaveBatchAsync([], stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        ServerLog.ItemSaveQueueError(logger, ex);
                    }
                    finally
                    {
                        flushMarker.SetResult();
                    }
                }
                else if (item is not null)
                {
                    // Coalesce anything already queued for the same item: during shutdown or
                    // a busy time the same id can appear several times, and only the last
                    // matters.
                    var batch = new Dictionary<Guid, ItemInstance> { [item.Id] = item };

                    while (queue.Reader.TryRead(out var extra))
                    {
                        if (extra.Item2 is not null)
                        {
                            // Another flush marker came in, handle pending saves first then queue it for later
                            if (batch.Count > 0)
                            {
                                try
                                {
                                    await SaveBatchAsync(batch.Values, stoppingToken);
                                }
                                catch (Exception ex) when (ex is not OperationCanceledException)
                                {
                                    ServerLog.ItemSaveQueueError(logger, ex);
                                }
                                batch.Clear();
                            }
                            extra.Item2.SetResult();
                        }
                        else if (extra.Item1 is not null)
                        {
                            batch[extra.Item1.Id] = extra.Item1;
                        }
                    }

                    if (batch.Count > 0)
                    {
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
