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
    private readonly Channel<CharacterSnapshot> _channel =
        Channel.CreateUnbounded<CharacterSnapshot>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });

    public ChannelReader<CharacterSnapshot> Reader => _channel.Reader;

    public void Enqueue(CharacterSnapshot snapshot) => _channel.Writer.TryWrite(snapshot);

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
            await foreach (var snapshot in queue.Reader.ReadAllAsync(stoppingToken))
            {
                // Coalesce anything already queued for the same character: during shutdown or
                // a busy autosave the same id can appear several times, and only the last
                // matters.
                var batch = new Dictionary<Guid, CharacterSnapshot> { [snapshot.Id] = snapshot };

                while (queue.Reader.TryRead(out var extra))
                {
                    batch[extra.Id] = extra;
                }

                try
                {
                    await SaveBatchAsync(batch.Values, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A failed save must not kill the worker, or every later save is lost too.
                    ServerLog.CharacterSaveFailed(logger, batch.Count, ex);
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
