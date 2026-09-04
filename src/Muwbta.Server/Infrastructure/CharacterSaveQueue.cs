using System.Threading.Channels;
using Muwbta.Engine;
using Muwbta.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Muwbta.Server.Infrastructure;

/// <summary>One unit of work for the character save queue.</summary>
public abstract record CharacterSaveJob;

public sealed record SaveCharacterJob(CharacterSnapshot Snapshot) : CharacterSaveJob;

/// <summary>A caller waiting for everything enqueued before it to be durable.</summary>
public sealed record FlushCharactersJob(TaskCompletionSource Completion) : CharacterSaveJob;

/// <summary>
/// The write side of the persistence hand-off. The game loop calls
/// <see cref="Enqueue"/> and moves on; nothing here blocks it.
/// </summary>
public sealed class CharacterSaveQueue : ICharacterSaveQueue
{
    private readonly Channel<CharacterSaveJob> _channel =
        Channel.CreateUnbounded<CharacterSaveJob>(new UnboundedChannelOptions
        {
            SingleReader = true,

            // Snapshots come from the loop, but a flush marker comes from whichever request
            // thread is logging a player out, so writes are not single-writer.
            SingleWriter = false,
        });

    public ChannelReader<CharacterSaveJob> Reader => _channel.Reader;

    public void Enqueue(CharacterSnapshot snapshot) =>
        _channel.Writer.TryWrite(new SaveCharacterJob(snapshot));

    /// <summary>
    /// Completes once every snapshot enqueued before this call has been written. Returns
    /// immediately if the queue is already closed - a caller waiting on a drained queue would
    /// wait forever, and a logout during shutdown must not hang or throw.
    /// </summary>
    public Task FlushAsync(CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_channel.Writer.TryWrite(new FlushCharactersJob(completion)))
        {
            return Task.CompletedTask;
        }

        return completion.Task.WaitAsync(cancellationToken);
    }

    public void Complete() => _channel.Writer.TryComplete();
}

/// <summary>
/// Drains save jobs and writes them to Postgres, entirely off the game loop thread.
/// </summary>
public sealed class CharacterSaveWorker(
    CharacterSaveQueue queue,
    IDbContextFactory<MuwbtaDbContext> factory,
    ILogger<CharacterSaveWorker> logger,
    Telemetry.ServerMetrics metrics) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // ReadAllAsync throws OperationCanceledException on shutdown, and an exception
        // escaping a BackgroundService is a fault: the host logs Critical and stops. During a
        // shutdown that is already under way, that Critical reaches logging providers midway
        // through disposal. Normal shutdown must not look like a fault.
        try
        {
            await foreach (var job in queue.Reader.ReadAllAsync(stoppingToken))
            {
                // Coalesce by character id: during shutdown or a busy autosave the same id can
                // appear several times, and only the last snapshot matters.
                var batch = new Dictionary<Guid, CharacterSnapshot>();
                var waiters = new List<TaskCompletionSource>();

                Absorb(job, batch, waiters);

                while (queue.Reader.TryRead(out var extra))
                {
                    Absorb(extra, batch, waiters);
                }

                try
                {
                    await SaveBatchAsync(batch.Values, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A failed save must not kill the worker, or every later save is lost too.
                    ServerLog.CharacterSaveFailed(logger, batch.Count, ex);
                    metrics.SaveFailed(batch.Count);
                }
                finally
                {
                    // Signal only after the batch is written, so FlushAsync means "durable".
                    // Draining first is the whole point: completing the marker while snapshots
                    // were still queued behind it let a logout return before the character's
                    // own final save had landed.
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
        while (queue.Reader.TryRead(out var leftover))
        {
            if (leftover is FlushCharactersJob flush)
            {
                flush.Completion.TrySetResult();
            }
        }
    }

    private static void Absorb(
        CharacterSaveJob job,
        Dictionary<Guid, CharacterSnapshot> batch,
        List<TaskCompletionSource> waiters)
    {
        switch (job)
        {
            case SaveCharacterJob save:
                batch[save.Snapshot.Id] = save.Snapshot;
                break;

            case FlushCharactersJob flush:
                waiters.Add(flush.Completion);
                break;
        }
    }

    private async Task SaveBatchAsync(
        IEnumerable<CharacterSnapshot> snapshots,
        CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        var saved = 0;

        foreach (var snapshot in snapshots)
        {
            // Load and copy rather than attaching the detached graph: the owned jsonb stat
            // blocks make attach-and-mark-modified fragile, and saves are rare enough that
            // the extra read costs nothing.
            var tracked = await db.Characters
                .FirstOrDefaultAsync(c => c.Id == snapshot.Id, cancellationToken);

            if (tracked is null)
            {
                continue;
            }

            tracked.RoomKey = snapshot.RoomKey;
            tracked.Level = snapshot.Level;
            tracked.Xp = snapshot.Xp;
            tracked.Attributes = snapshot.Attributes;
            tracked.Vitals = snapshot.Vitals;

            // Both of these are captured by CharacterSnapshot.From and were being dropped on
            // the floor here, so gold earned from kills and sales, and any bind point set with
            // `bind`, did not survive a restart.
            tracked.Gold = snapshot.Gold;
            tracked.RespawnRoomKey = snapshot.RespawnRoomKey;

            // And this is the third field that would have been dropped here. A capability that
            // does not survive a restart is worse than one that was never granted: the quest is
            // Completed, so the chain cannot be re-run to earn it again (PLAN.md §4.15).
            tracked.Flags = [.. snapshot.Flags];

            // The fourth field this step could have dropped. An ignore list that did not survive
            // a restart would put the pest back the next morning.
            tracked.IgnoredNames = [.. snapshot.IgnoredNames];

            tracked.LastPlayedAt = snapshot.LastPlayedAt;
            tracked.PlaytimeSeconds = snapshot.PlaytimeSeconds;
            saved++;
        }

        if (saved > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
