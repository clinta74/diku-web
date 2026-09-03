using System.Threading.Channels;
using Muwbta.Engine;
using Muwbta.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Muwbta.Server.Infrastructure;

/// <summary>
/// One unit of work for the quest save queue. Shaped like <see cref="ItemSaveJob"/> because
/// abandoning a quest deletes its row rather than setting a status: §6 says no row means not
/// started, so the queue has to carry deletes as well as snapshots.
/// </summary>
public abstract record QuestSaveJob(Guid CharacterId, string QuestKey);

public sealed record SaveQuestJob(CharacterQuestSnapshot Snapshot)
    : QuestSaveJob(Snapshot.CharacterId, Snapshot.QuestKey);

/// <summary>Carries only the pair: the state is already gone from the world by this point.</summary>
public sealed record DeleteQuestJob(Guid CharacterId, string QuestKey)
    : QuestSaveJob(CharacterId, QuestKey);

/// <summary>
/// The write side of the quest state persistence hand-off. The game loop calls
/// <see cref="Enqueue"/> or <see cref="EnqueueDelete"/> and moves on; nothing here blocks it.
/// </summary>
public sealed class CharacterQuestSaveQueue : ICharacterQuestSaveQueue
{
    private readonly Channel<QuestSaveJob> _channel =
        Channel.CreateUnbounded<QuestSaveJob>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });

    public ChannelReader<QuestSaveJob> Reader => _channel.Reader;

    public void Enqueue(CharacterQuestSnapshot snapshot) =>
        _channel.Writer.TryWrite(new SaveQuestJob(snapshot));

    public void EnqueueDelete(Guid characterId, string questKey) =>
        _channel.Writer.TryWrite(new DeleteQuestJob(characterId, questKey));

    public void Complete() => _channel.Writer.TryComplete();
}

/// <summary>
/// Drains quest save jobs and writes them to Postgres, entirely off the game loop thread.
/// </summary>
public sealed class CharacterQuestSaveWorker(
    CharacterQuestSaveQueue queue,
    IDbContextFactory<MuwbtaDbContext> factory,
    ILogger<CharacterQuestSaveWorker> logger) : BackgroundService
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
                // Coalesce anything already queued for the same character/quest: during shutdown or
                // a busy autosave the same pair can appear several times, and only the last matters.
                // Keying deletes the same way is what makes accept-then-abandon in one batch settle
                // on the abandon rather than racing.
                var batch = new Dictionary<(Guid, string), QuestSaveJob>
                {
                    [(job.CharacterId, job.QuestKey)] = job
                };

                while (queue.Reader.TryRead(out var extra))
                {
                    batch[(extra.CharacterId, extra.QuestKey)] = extra;
                }

                try
                {
                    await SaveBatchAsync(batch.Values, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    ServerLog.QuestSaveQueueError(logger, ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }

    private async Task SaveBatchAsync(IEnumerable<QuestSaveJob> jobs, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        foreach (var job in jobs)
        {
            // Find the existing character_quests row
            var existing = await db.CharacterQuests
                .FirstOrDefaultAsync(
                    cq => cq.CharacterId == job.CharacterId && cq.QuestKey == job.QuestKey,
                    ct);

            if (job is DeleteQuestJob)
            {
                // Absence is the state being written. Nothing to do when there is no row - a
                // quest abandoned before its first save has never reached storage at all.
                if (existing is not null)
                {
                    db.CharacterQuests.Remove(existing);
                }

                continue;
            }

            var snapshot = ((SaveQuestJob)job).Snapshot;

            if (existing is not null)
            {
                // Update existing row
                existing.Status = snapshot.Status;
                existing.CompletedAt = snapshot.CompletedAt;
                existing.TimesCompleted = snapshot.TimesCompleted;
                db.CharacterQuests.Update(existing);
            }
            else
            {
                // Insert new row
                var newQuest = new Muwbta.Domain.Quests.CharacterQuest
                {
                    CharacterId = snapshot.CharacterId,
                    QuestKey = snapshot.QuestKey,
                    Status = snapshot.Status,
                    StartedAt = snapshot.StartedAt,
                    CompletedAt = snapshot.CompletedAt,
                    TimesCompleted = snapshot.TimesCompleted,
                };
                db.CharacterQuests.Add(newQuest);
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
