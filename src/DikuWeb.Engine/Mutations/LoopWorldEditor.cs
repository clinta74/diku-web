namespace DikuWeb.Engine.Mutations;

/// <summary>
/// The edit path for code already running on the game loop - that is, the in-game builder
/// commands (PLAN.md §7.6).
/// </summary>
/// <remarks>
/// A command handler cannot go through <c>GameGateway.MutateAsync</c>: it is already on the
/// loop thread, so enqueueing a message and awaiting the loop to drain it would wait for a
/// pulse that cannot begin until the current one finishes. That is a deadlock, not a delay.
///
/// So it applies directly and queues the resulting primitives for persistence.
/// </remarks>
public sealed class LoopWorldEditor(WorldMutationApplier applier, IWorldWriteQueue writes)
{
    public MutationResult Apply(WorldChange change, Guid? accountId)
    {
        ArgumentNullException.ThrowIfNull(change);

        var result = applier.Apply(change);

        if (result.Success && result.Applied.Count > 0)
        {
            writes.Enqueue(new WorldWriteJob(result.Applied, accountId));
        }

        return result;
    }
}
