using System.Threading.Channels;
using Muwbta.Engine.Mutations;
using Muwbta.Engine.Protocol;

namespace Muwbta.Engine;

/// <summary>
/// The Server's only door into the Engine. HTTP handlers hand messages to this and never
/// touch world state (PLAN.md §2.1).
/// </summary>
public sealed class GameGateway
{
    /// <summary>Generous next to the 250 ms pulse: this catches a stopped loop, not a slow one.</summary>
    private static readonly TimeSpan MutationTimeout = TimeSpan.FromSeconds(10);

    private readonly Channel<InboundMessage> _inbound;

    public GameGateway(EngineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _inbound = Channel.CreateBounded<InboundMessage>(new BoundedChannelOptions(options.InboundCapacity)
        {
            // Wait rather than drop, but callers use TryWrite and surface backpressure as
            // 429 instead of blocking a request thread.
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    internal ChannelReader<InboundMessage> Reader => _inbound.Reader;

    /// <summary>
    /// Queues a message for the game loop. Returns false when the queue is saturated, which
    /// the caller should surface as 429 rather than retrying - a full inbound queue means the
    /// loop is already behind.
    /// </summary>
    public bool TrySubmit(InboundMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return _inbound.Writer.TryWrite(message);
    }

    /// <summary>
    /// Submits a builder edit and waits for the loop to apply it (PLAN.md §7.3).
    /// </summary>
    /// <remarks>
    /// The timeout is a liveness guard, not a latency target. A mutation normally lands within
    /// one pulse; if the loop is wedged or has stopped, a builder should get an error rather
    /// than a request that hangs until the browser gives up.
    /// </remarks>
    public async Task<MutationResult> MutateAsync(
        WorldChange change,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);

        var completion = new TaskCompletionSource<MutationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        if (!TrySubmit(new WorldMutation { Change = change, Completion = completion }))
        {
            return MutationResult.Fail(MutationError.Invalid, "The server is busy. Try again.");
        }

        return await AwaitOrTimeout(
            completion.Task,
            MutationResult.Fail(MutationError.Invalid, "The world did not respond in time."),
            cancellationToken);
    }

    /// <summary>
    /// Submits many builder edits at once and waits for the loop to apply all of them.
    /// </summary>
    /// <returns>One result per change, positionally.</returns>
    /// <remarks>
    /// <para>
    /// <b>The loop already drains up to 512 messages a pulse; this is what lets a caller use that.</b>
    /// Calling <see cref="MutateAsync"/> in a loop pays a full 250ms pulse per change, because each
    /// call waits for its own completion before the next is submitted. An import of the Reaches
    /// bundle is 1005 entities, which is a little over four minutes of doing nothing but waiting for
    /// pulses. Submitting the batch first and awaiting it afterwards lands the whole thing in one
    /// pulse.
    /// </para>
    /// <para>
    /// <b>Order is preserved, and that is load-bearing.</b> The channel is FIFO and
    /// <see cref="TrySubmit"/> runs synchronously here, so the loop applies these in the order given
    /// - which is what lets a caller batch a set of rooms and then a set of exits that point at them.
    /// </para>
    /// <para>
    /// A change the queue has no room for gets its own "server is busy" result rather than taking the
    /// batch down with it, and is not awaited - so a saturated queue answers immediately instead of
    /// spending the timeout.
    /// </para>
    /// <para>
    /// The timeout covers the whole batch rather than each change. It is the same liveness guard
    /// <see cref="MutateAsync"/> uses and for the same reason: a wedged loop should produce an error,
    /// not a request that hangs. A caller submitting more than the loop drains in one pulse should
    /// chunk - see <c>WorldImporter</c>, which does.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<MutationResult>> MutateManyAsync(
        IReadOnlyList<WorldChange> changes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);

        if (changes.Count == 0)
        {
            return [];
        }

        var results = new MutationResult?[changes.Count];
        var pending = new List<(int Index, Task<MutationResult> Task)>(changes.Count);

        for (var i = 0; i < changes.Count; i++)
        {
            ArgumentNullException.ThrowIfNull(changes[i]);

            var completion = new TaskCompletionSource<MutationResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            if (TrySubmit(new WorldMutation { Change = changes[i], Completion = completion }))
            {
                pending.Add((i, completion.Task));
            }
            else
            {
                results[i] = MutationResult.Fail(MutationError.Invalid, "The server is busy. Try again.");
            }
        }

        if (pending.Count > 0)
        {
            var timedOut = MutationResult.Fail(MutationError.Invalid, "The world did not respond in time.");

            // One wait for the batch. Awaiting each task in turn would be correct but would give the
            // last change in a batch a timeout budget measured from when the first one was awaited,
            // which reads as an arbitrary limit on batch size.
            await AwaitOrTimeout(
                Task.WhenAll(pending.Select(p => p.Task)),
                Array.Empty<MutationResult>(),
                cancellationToken);

            foreach (var (index, task) in pending)
            {
                results[index] = task.IsCompletedSuccessfully ? task.Result : timedOut;
            }
        }

        // Never null by construction: every index is filled either by a refusal above, or by its
        // completion, or by the timeout. The cast keeps that a compile-time fact rather than a
        // comment.
        return [.. results.Select(r => r ?? MutationResult.Fail(
            MutationError.Invalid, "The world did not respond in time."))];
    }

    /// <summary>Swaps the loaded world for freshly-read data. See <see cref="ReplaceWorld"/>.</summary>
    public async Task<bool> ReplaceWorldAsync(
        WorldData data,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        return TrySubmit(new ReplaceWorld { Data = data, Completion = completion })
            && await AwaitOrTimeout(completion.Task, false, cancellationToken);
    }

    private static async Task<T> AwaitOrTimeout<T>(
        Task<T> task,
        T onTimeout,
        CancellationToken cancellationToken)
    {
        try
        {
            return await task.WaitAsync(MutationTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            return onTimeout;
        }
    }

    internal void Complete() => _inbound.Writer.TryComplete();
}
