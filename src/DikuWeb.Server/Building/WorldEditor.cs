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
    BuilderChangeFeed feed,
    ILogger<WorldEditor> logger)
{
    /// <summary>
    /// One edit. A batch of one, so there is a single code path rather than two that can drift.
    /// </summary>
    public async Task<EditOutcome> ApplyAsync(
        WorldChange change,
        Guid? accountId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        return (await ApplyManyAsync([change], accountId, cancellationToken))[0];
    }

    /// <summary>
    /// Many edits, applied together: one trip through the loop and one transaction for the lot.
    /// </summary>
    /// <returns>One outcome per change, positionally.</returns>
    /// <remarks>
    /// <para>
    /// <b>Why this exists.</b> Applying one change at a time costs a 250ms pulse each, because each
    /// call waits for its own completion before submitting the next - and the loop will drain 512
    /// messages in a single pulse. An import of a thousand entities spent four minutes waiting for
    /// pulses. See <c>GameGateway.MutateManyAsync</c>.
    /// </para>
    /// <para>
    /// <b>The order of the two halves is the same as it ever was</b>, and for the reason in the class
    /// remarks: memory first, because only the loop can validate against live world state, then
    /// persist, then announce. Batching changes how many changes ride in each half, not which comes
    /// first.
    /// </para>
    /// <para>
    /// <b>One transaction for the batch, which is stronger than before, not weaker.</b> Every applied
    /// primitive goes into a single <see cref="WorldWriter.WriteAsync"/> call, which already wraps its
    /// whole list in one transaction with a save per step. So a write failure now rolls back the
    /// batch instead of leaving part of it committed. The caller still chunks, so this is bounded.
    /// </para>
    /// <para>
    /// A change the loop refuses is reported as refused and contributes nothing to the write. The
    /// rest of the batch still lands - one bad room in a bundle should not cost the other two
    /// hundred, which is the same judgement <c>WorldImporter</c> already makes per entity.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<EditOutcome>> ApplyManyAsync(
        IReadOnlyList<WorldChange> changes,
        Guid? accountId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);

        if (changes.Count == 0)
        {
            return [];
        }

        var results = await gateway.MutateManyAsync(changes, cancellationToken);

        // Every primitive the loop actually applied, in the order it applied them - which is what
        // the writer needs, since a rename must create the new room before exits can point at it.
        var applied = results.Where(r => r.Success).SelectMany(r => r.Applied).ToList();

        if (applied.Count == 0)
        {
            return [.. results.Select(r => new EditOutcome(
                r.Success ? EditStatus.Saved : EditStatus.Refused, r))];
        }

        try
        {
            await writer.WriteAsync(applied, accountId, cancellationToken);

            // Only after the write is durable - a feed fired before this could announce an edit
            // that the catch below then rolls back. Notify per affected primitive so a rename,
            // which touches several rooms, refreshes all of them.
            foreach (var change in applied)
            {
                feed.Publish(new BuilderChange(
                    change.EntityKind, change.EntityKey, ActionOf(change), accountId));
            }

            return [.. results.Select(r => new EditOutcome(
                r.Success ? EditStatus.Saved : EditStatus.Refused, r))];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Logged against the first change of the batch: the transaction rolled back as a unit,
            // so naming one of them is honest and naming all of them would be a hundred lines
            // saying the same thing.
            ServerLog.MutationNotPersisted(
                logger, applied[0].EntityKind, applied[0].EntityKey, ex);

            // Still the right recovery, and more obviously so for a batch than for one edit: the
            // loop has applied all of these to memory and none of them are durable.
            await ResyncAsync(cancellationToken);

            return [.. results.Select(r => new EditOutcome(
                r.Success ? EditStatus.NotSaved : EditStatus.Refused, r))];
        }
    }

    /// <summary>Whether a primitive removes its entity, for the feed's advisory action field.</summary>
    internal static string ActionOf(WorldChange change) =>
        change.GetType().Name.StartsWith("Delete", StringComparison.Ordinal) ? "delete" : "update";

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
