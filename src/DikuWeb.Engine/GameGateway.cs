using System.Threading.Channels;
using DikuWeb.Engine.Mutations;
using DikuWeb.Engine.Protocol;

namespace DikuWeb.Engine;

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
