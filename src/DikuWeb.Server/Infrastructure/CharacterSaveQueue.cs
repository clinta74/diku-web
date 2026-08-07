using System.Threading.Channels;
using DikuWeb.Engine;
using DikuWeb.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DikuWeb.Server.Infrastructure;

/// <summary>
/// The write side of the persistence hand-off. The game loop calls
/// <see cref="Enqueue"/> and moves on; nothing here blocks it.
/// </summary>
public sealed class CharacterSaveQueue : ICharacterSaveQueue
{
    private readonly Channel<(CharacterSnapshot?, TaskCompletionSource?)> _channel =
        Channel.CreateUnbounded<(CharacterSnapshot?, TaskCompletionSource?)>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });

    public ChannelReader<(CharacterSnapshot?, TaskCompletionSource?)> Reader => _channel.Reader;

    public void Enqueue(CharacterSnapshot snapshot) => _channel.Writer.TryWrite((snapshot, null));

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource();
        if (!_channel.Writer.TryWrite((null, tcs)))
        {
            tcs.SetException(new InvalidOperationException("Character save queue is closed"));
        }
        await tcs.Task.ConfigureAwait(false);
    }

    public void Complete() => _channel.Writer.TryComplete();
}

/// <summary>
/// Drains save jobs and writes them to Postgres, entirely off the game loop thread.
/// </summary>
public sealed class CharacterSaveWorker(
    CharacterSaveQueue queue,
    IDbContextFactory<DikuWebDbContext> factory,
    ILogger<CharacterSaveWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // ReadAllAsync throws OperationCanceledException on shutdown, and an exception
        // escaping a BackgroundService is a fault: the host logs Critical and stops. During a
        // shutdown that is already under way, that Critical reaches logging providers midway
        // through disposal. Normal shutdown must not look like a fault.
        try
        {
            await foreach (var (snapshot, flushMarker) in queue.Reader.ReadAllAsync(stoppingToken))
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
                        ServerLog.CharacterSaveFailed(logger, 0, ex);
                    }
                    finally
                    {
                        flushMarker.SetResult();
                    }
                }
                else if (snapshot is not null)
                {
                    // Coalesce anything already queued for the same character: during shutdown or
                    // a busy autosave the same id can appear several times, and only the last
                    // matters.
                    var batch = new Dictionary<Guid, CharacterSnapshot> { [snapshot.Id] = snapshot };

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
                                    ServerLog.CharacterSaveFailed(logger, batch.Count, ex);
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
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            ServerLog.CharacterSaveFailed(logger, batch.Count, ex);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
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
