using System.Threading.Channels;
using DikuWeb.Engine.Protocol;

namespace DikuWeb.Engine;

/// <summary>
/// The Server's only door into the Engine. HTTP handlers hand messages to this and never
/// touch world state (PLAN.md §2.1).
/// </summary>
public sealed class GameGateway
{
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

    internal void Complete() => _inbound.Writer.TryComplete();
}
