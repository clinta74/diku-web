using System.Collections.Concurrent;
using System.Threading.Channels;

namespace DikuWeb.Server.Building;

/// <summary>One persisted builder edit, broadcast to every open builder stream (PLAN §2).</summary>
/// <param name="Kind">The entity kind, matching <c>WorldChange.EntityKind</c>: "room", "zone", …</param>
/// <param name="Key">The entity key that changed.</param>
/// <param name="Action">"update" or "delete" - advisory; the client reloads either way.</param>
/// <param name="ByAccountId">Who made the edit, so a builder can skip echoes of their own work.</param>
public sealed record BuilderChange(string Kind, string Key, string Action, Guid? ByAccountId);

/// <summary>
/// A fan-out of content edits to connected builders, so a second builder's screen updates when
/// someone else saves. Deliberately fired on <em>persistence success</em>, not on loop apply:
/// a write that fails is rolled back by a full reload, and a feed fired at apply time would have
/// already announced a change that then vanished.
/// </summary>
/// <remarks>
/// This is separate from the game SSE stream, which pushes to players standing in a room. That
/// stream has a session per character; this has no session - any authorised builder may listen,
/// and every subscriber sees every edit.
/// </remarks>
public sealed class BuilderChangeFeed
{
    // Bounded so a stalled reader cannot grow without limit; a builder that falls behind drops
    // the oldest events and reloads on the next one it does see, which is self-correcting.
    private readonly ConcurrentDictionary<Guid, Channel<BuilderChange>> _subscribers = new();

    public IDisposable Subscribe(out ChannelReader<BuilderChange> reader)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<BuilderChange>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        _subscribers[id] = channel;
        reader = channel.Reader;
        return new Subscription(this, id);
    }

    public void Publish(BuilderChange change)
    {
        foreach (var channel in _subscribers.Values)
        {
            channel.Writer.TryWrite(change);
        }
    }

    private void Unsubscribe(Guid id)
    {
        if (_subscribers.TryRemove(id, out var channel))
        {
            channel.Writer.TryComplete();
        }
    }

    private sealed class Subscription(BuilderChangeFeed feed, Guid id) : IDisposable
    {
        public void Dispose() => feed.Unsubscribe(id);
    }
}
