using Muwbta.Domain.Worlds;
using Muwbta.Engine;
using Muwbta.Engine.Mutations;
using Muwbta.Engine.Protocol;

namespace Muwbta.Engine.Tests.Mutations;

/// <summary>
/// Submitting many mutations at once, which is what makes an import take seconds (PLAN.md §6).
/// </summary>
/// <remarks>
/// <para>
/// <b>The property everything else rests on: a batch costs one pulse, not one pulse each.</b>
/// <c>MutateAsync</c> waits for its own completion before returning, so a caller looping over it pays
/// a full 250ms pulse per change — which is why importing 1005 entities took about four minutes and
/// outlived every sensible proxy timeout. The loop has always drained up to 512 messages in a single
/// pulse; nothing could use that until there was a way to submit without awaiting.
/// </para>
/// <para>
/// These drain the channel by hand rather than running a <c>GameLoop</c>. The point is what the
/// gateway hands over and in what order, and a real loop would bring a whole world along to answer a
/// question about a queue.
/// </para>
/// </remarks>
public sealed class GameGatewayBatchTests
{
    private static GameGateway NewGateway(int capacity = 4096) =>
        new(new EngineOptions { InboundCapacity = capacity });

    private static UpsertWorld Change(string key) =>
        new(key, key, string.Empty, 0, new(), new());

    /// <summary>
    /// Drains everything queued and completes each one, the way <c>GameLoop.DrainInbound</c> does.
    /// </summary>
    /// <returns>The changes it saw, in the order it saw them.</returns>
    private static List<string> DrainOnce(GameGateway gateway, int max = 512)
    {
        var seen = new List<string>();

        while (seen.Count < max && gateway.Reader.TryRead(out var message))
        {
            if (message is WorldMutation mutation)
            {
                seen.Add(mutation.Change.EntityKey);
                mutation.Completion.TrySetResult(MutationResult.Ok([mutation.Change]));
            }
        }

        return seen;
    }

    // -----------------------------------------------------------------------
    // One pulse, not N
    // -----------------------------------------------------------------------

    /// <summary>
    /// <b>The regression test for the whole change.</b> A batch of 128 is entirely in the queue
    /// before anything is awaited, so a single drain finishes all of it. Reintroduce a per-change
    /// await anywhere in the chain and this deadlocks rather than passing slowly, which is the
    /// failure mode worth having.
    /// </summary>
    [Fact]
    public async Task A_whole_batch_is_queued_before_any_of_it_is_awaited()
    {
        var gateway = NewGateway();
        var changes = Enumerable.Range(0, 128).Select(i => Change($"w{i}")).ToList();

        var batch = gateway.MutateManyAsync(changes, CancellationToken.None);

        // Nothing has drained yet, and the whole batch is already sitting in the queue - which is
        // exactly what the old one-at-a-time path could not do.
        var seen = DrainOnce(gateway);
        Assert.Equal(128, seen.Count);

        var results = await batch;
        Assert.Equal(128, results.Count);
        Assert.All(results, r => Assert.True(r.Success));
    }

    /// <summary>
    /// Order is preserved, which is what lets a caller batch rooms and then the exits that point at
    /// them.
    /// </summary>
    [Fact]
    public async Task The_loop_sees_the_batch_in_the_order_it_was_given()
    {
        var gateway = NewGateway();
        var changes = Enumerable.Range(0, 64).Select(i => Change($"w{i:D2}")).ToList();

        var batch = gateway.MutateManyAsync(changes, CancellationToken.None);
        var seen = DrainOnce(gateway);
        await batch;

        Assert.Equal([.. changes.Select(c => c.Key)], seen);
    }

    /// <summary>
    /// Results come back positionally, so a caller can put each one beside the entity it came from —
    /// which is how the import report keeps naming individual keys.
    /// </summary>
    [Fact]
    public async Task Results_line_up_with_the_changes_they_answer()
    {
        var gateway = NewGateway();
        var changes = Enumerable.Range(0, 8).Select(i => Change($"w{i}")).ToList();

        var batch = gateway.MutateManyAsync(changes, CancellationToken.None);

        // Refuse the third one specifically, so a positional mix-up cannot pass.
        while (gateway.Reader.TryRead(out var message))
        {
            if (message is not WorldMutation mutation)
            {
                continue;
            }

            mutation.Completion.TrySetResult(
                mutation.Change.EntityKey == "w2"
                    ? MutationResult.Fail(MutationError.Invalid, "no")
                    : MutationResult.Ok([mutation.Change]));
        }

        var results = await batch;

        Assert.False(results[2].Success);
        Assert.All(results.Where((_, i) => i != 2), r => Assert.True(r.Success));
    }

    // -----------------------------------------------------------------------
    // Edges
    // -----------------------------------------------------------------------

    [Fact]
    public async Task An_empty_batch_asks_the_loop_nothing()
    {
        var gateway = NewGateway();

        Assert.Empty(await gateway.MutateManyAsync([], CancellationToken.None));
        Assert.False(gateway.Reader.TryRead(out _));
    }

    /// <summary>
    /// A saturated queue answers immediately for the overflow rather than spending the timeout on it,
    /// and the changes that did get in still get real answers.
    /// </summary>
    [Fact]
    public async Task A_full_queue_refuses_the_overflow_and_keeps_the_rest()
    {
        var gateway = NewGateway(capacity: 4);
        var changes = Enumerable.Range(0, 10).Select(i => Change($"w{i}")).ToList();

        var batch = gateway.MutateManyAsync(changes, CancellationToken.None);
        DrainOnce(gateway);

        var results = await batch;

        Assert.Equal(10, results.Count);
        Assert.Equal(4, results.Count(r => r.Success));
        Assert.All(
            results.Where(r => !r.Success),
            r => Assert.Contains("busy", r.Message ?? "", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A batch bigger than one drain still completes — it simply takes another. This is what makes
    /// the caller's chunking a tuning decision rather than a correctness requirement.
    /// </summary>
    [Fact]
    public async Task A_batch_larger_than_one_drain_finishes_across_two()
    {
        var gateway = NewGateway();
        var changes = Enumerable.Range(0, 700).Select(i => Change($"w{i}")).ToList();

        var batch = gateway.MutateManyAsync(changes, CancellationToken.None);

        Assert.Equal(512, DrainOnce(gateway).Count);
        Assert.Equal(188, DrainOnce(gateway).Count);

        Assert.Equal(700, (await batch).Count);
    }
}
