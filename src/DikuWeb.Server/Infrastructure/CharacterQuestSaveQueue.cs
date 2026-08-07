using System.Threading.Channels;
using DikuWeb.Engine;
using DikuWeb.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DikuWeb.Server.Infrastructure;

/// <summary>
/// The write side of the quest state persistence hand-off. The game loop calls
/// <see cref="Enqueue"/> and moves on; nothing here blocks it.
/// </summary>
public sealed class CharacterQuestSaveQueue : ICharacterQuestSaveQueue
{
    private readonly Channel<CharacterQuestSnapshot> _channel =
        Channel.CreateUnbounded<CharacterQuestSnapshot>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });

    public ChannelReader<CharacterQuestSnapshot> Reader => _channel.Reader;

    public void Enqueue(CharacterQuestSnapshot snapshot) => _channel.Writer.TryWrite(snapshot);

    public void Complete() => _channel.Writer.TryComplete();
}

/// <summary>
/// Drains quest save jobs and writes them to Postgres, entirely off the game loop thread.
/// </summary>
public sealed class CharacterQuestSaveWorker(
    CharacterQuestSaveQueue queue,
    IDbContextFactory<DikuWebDbContext> factory,
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
            await foreach (var snapshot in queue.Reader.ReadAllAsync(stoppingToken))
            {
                // Coalesce anything already queued for the same character/quest: during shutdown or
                // a busy autosave the same pair can appear several times, and only the last matters.
                var batch = new Dictionary<(Guid, string), CharacterQuestSnapshot>
                {
                    [(snapshot.CharacterId, snapshot.QuestKey)] = snapshot
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

    private async Task SaveBatchAsync(IEnumerable<CharacterQuestSnapshot> snapshots, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        foreach (var snapshot in snapshots)
        {
            // Find the existing character_quests row
            var existing = await db.CharacterQuests
                .FirstOrDefaultAsync(
                    cq => cq.CharacterId == snapshot.CharacterId && cq.QuestKey == snapshot.QuestKey,
                    ct);

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
                var newQuest = new DikuWeb.Domain.Quests.CharacterQuest
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
