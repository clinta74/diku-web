using DikuWeb.Engine;
using DikuWeb.Engine.Mutations;

namespace DikuWeb.Server.Building;

/// <summary>
/// One builder edit, end to end: enqueue → the loop applies → persist → notify (PLAN.md §7.3).
/// </summary>
/// <remarks>
/// The interesting part is the order and what happens when the second half fails.
///
/// Memory changes first, because only the loop can validate against live world state - "is
/// anyone standing in this zone?" cannot be answered from the database. That leaves a window
/// where the edit is visible to players but not yet durable. If persistence then fails, the
/// edit would survive until a restart silently discarded it, which is the worst of both: the
/// builder saw success, players saw the change, and it evaporates hours later.
///
/// So a failed write is followed by a full reload from Postgres. It is a blunt recovery -
/// every room, not just the failed one - but it is correct, it is rare (a failure here means
/// the database is unreachable, not that the edit was bad), and it restores the invariant that
/// memory equals durable state. The builder gets a 500 and a truthful message.
/// </remarks>
public sealed class WorldEditor(
    GameGateway gateway,
    WorldWriter writer,
    IWorldSource worldSource,
    ILogger<WorldEditor> logger)
{
    public async Task<EditOutcome> ApplyAsync(
        WorldChange change,
        Guid? accountId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        var result = await gateway.MutateAsync(change, cancellationToken);

        if (!result.Success)
        {
            return new EditOutcome(EditStatus.Refused, result);
        }

        try
        {
            await writer.WriteAsync(result.Applied, accountId, cancellationToken);
            return new EditOutcome(EditStatus.Saved, result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ServerLog.MutationNotPersisted(logger, change.EntityKind, change.EntityKey, ex);
            await ResyncAsync(cancellationToken);
            return new EditOutcome(EditStatus.NotSaved, result);
        }
    }

    /// <summary>
    /// Reloads the world from the database and hands it to the loop, putting memory back in
    /// agreement with durable state.
    /// </summary>
    private async Task ResyncAsync(CancellationToken cancellationToken)
    {
        try
        {
            var data = await worldSource.LoadAsync(cancellationToken);

            if (!await gateway.ReplaceWorldAsync(data, cancellationToken))
            {
                ServerLog.ResyncFailed(logger);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Nothing left to try. The world in memory is now known to be ahead of the
            // database, which is exactly what this log line is for.
            ServerLog.ResyncThrew(logger, ex);
        }
    }
}

public enum EditStatus
{
    /// <summary>Applied and durable.</summary>
    Saved = 0,

    /// <summary>Rejected by the loop before anything changed.</summary>
    Refused = 1,

    /// <summary>Applied to memory, failed to persist, and rolled back by a reload.</summary>
    NotSaved = 2,
}

public sealed record EditOutcome(EditStatus Status, MutationResult Result)
{
    public bool Ok => Status == EditStatus.Saved;
}
