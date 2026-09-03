using System.Collections.Concurrent;
using System.Threading.Channels;
using Muwbta.Engine.Protocol;

namespace Muwbta.Server.Game;

/// <summary>
/// One character's connection to the world. Outlives any single SSE response, which is what
/// makes the link-dead grace window and Last-Event-ID replay work (PLAN.md §3.6).
/// </summary>
public sealed class GameSession
{
    /// <summary>PLAN.md §3.4: 250 events, enough to cover a reconnect on a flaky network.</summary>
    private const int RingCapacity = 250;

    private readonly Queue<(long Id, OutboundEvent Event)> _ring = new(RingCapacity);
    private readonly Lock _ringLock = new();

    private long _lastEventId;

    public GameSession()
    {
        Events = Channel.CreateBounded<OutboundEvent>(new BoundedChannelOptions(1024)
        {
            // A hopelessly slow client loses its oldest pending events rather than growing
            // the server's memory without bound. The ring buffer still covers a reconnect.
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public Guid Id { get; } = Guid.CreateVersion7();

    /// <summary>Checked on every request: a character may only be driven by its owner.</summary>
    public required Guid AccountId { get; init; }

    public required Guid CharacterId { get; init; }

    public required string CharacterName { get; init; }

    public Channel<OutboundEvent> Events { get; }

    /// <summary>
    /// True while an SSE response is actively draining this session.
    /// </summary>
    /// <remarks>
    /// Set by <see cref="StreamOwnership"/> rather than by the response, because who is streaming
    /// is a fact about the character rather than about this object — and this object is replaced
    /// every time somebody enters.
    /// </remarks>
    public bool IsStreaming { get; internal set; }

    /// <summary>
    /// When this client last proved it was still there.
    /// </summary>
    /// <remarks>
    /// <b>Not the last time we wrote to it, which proves nothing.</b> A TCP write into a kernel
    /// send buffer succeeds long after the peer has stopped acknowledging it, so the fifteen-second
    /// SSE heartbeat is a keep-alive for proxies rather than a liveness check — measured, a client
    /// whose network vanished silently was still holding a live session seventeen minutes later,
    /// with or without nginx in front (PLAN.md §11). This is the other direction: the client says
    /// something, so somebody is there.
    /// </remarks>
    public DateTimeOffset LastSeenAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Whether this client has ever sent a heartbeat, and so may be held to a deadline.
    /// </summary>
    /// <remarks>
    /// <b>The migration hinge, and the reason reaping is safe to turn on.</b> A browser holding a
    /// cached build from before heartbeats existed sends none — and reaping it for that would take
    /// a perfectly healthy player out of the world for running yesterday's JavaScript. So a session
    /// is only ever held to the deadline once it has proved, at least once, that it knows how to
    /// meet it. Old clients keep exactly the behaviour they have today: nothing better, nothing
    /// worse, and no cliff on the day this deploys.
    /// </remarks>
    public bool SendsHeartbeats { get; private set; }

    private int _reaped;

    /// <summary>Records that the client is still there.</summary>
    public void Seen(DateTimeOffset at, bool heartbeat)
    {
        LastSeenAt = at;

        if (heartbeat)
        {
            SendsHeartbeats = true;
        }
    }

    /// <summary>
    /// Claims this session for reaping, returning false if it was already claimed.
    /// </summary>
    /// <remarks>
    /// The loop leaves a session in the registry for the whole link-dead grace window, so without
    /// this the sweep would re-submit every six seconds for ninety seconds. The loop ignores the
    /// repeats, but the log would report fifteen disconnections where one happened.
    /// </remarks>
    public bool MarkReaped() => Interlocked.Exchange(ref _reaped, 1) == 0;

    /// <summary>Whether the liveness sweep has already given up on this session.</summary>
    public bool IsReaped => Volatile.Read(ref _reaped) == 1;

    /// <summary>
    /// The id the next recorded event would get, without taking it.
    /// </summary>
    /// <remarks>
    /// For frames that are about the *connection* rather than about the character — the displaced
    /// notice is the only one. It must carry an <c>id:</c> because a client tracks the last one it
    /// saw and sends it back on reconnect, and it must not consume one or enter the ring buffer:
    /// replaying "you were replaced" to the connection that did the replacing is exactly the
    /// message that would make it close itself.
    /// </remarks>
    public long PeekNextEventId()
    {
        lock (_ringLock)
        {
            return _lastEventId;
        }
    }

    /// <summary>Assigns the next id and records the event for possible replay.</summary>
    public long Record(OutboundEvent gameEvent)
    {
        lock (_ringLock)
        {
            var id = ++_lastEventId;
            _ring.Enqueue((id, gameEvent));

            while (_ring.Count > RingCapacity)
            {
                _ring.Dequeue();
            }

            return id;
        }
    }

    /// <summary>
    /// Events after <paramref name="lastSeenId"/> that are still in the buffer. An empty
    /// result means either nothing was missed or the client fell too far behind to catch up.
    /// </summary>
    public List<(long Id, OutboundEvent Event)> Replay(long lastSeenId)
    {
        lock (_ringLock)
        {
            return [.. _ring.Where(entry => entry.Id > lastSeenId)];
        }
    }
}

/// <summary>
/// Who is allowed to stream one character, kept per <b>character</b> rather than per session.
/// </summary>
/// <remarks>
/// <b>Per character is the whole correction.</b> Entering the world builds a <em>new</em>
/// <see cref="GameSession"/>, so ownership recorded on the session was thrown away at exactly the
/// moment a second device arrived — which is how the first attempt at this still let the two
/// devices trade the stream. Written down because it is not obvious from either type: a session is
/// a connection's worth of state, and "one character, one live stream" is a claim about the
/// character, which outlives every session it has.
///
/// A connection id is minted by the client per stream it opens. It is what makes any of this
/// answerable: two devices playing one character send byte-identical requests — same cookie, same
/// route — so nothing else distinguishes the device that was replaced from the one that replaced
/// it. It survives <c>EventSource</c>'s automatic retry, because a retry re-requests the same URL,
/// while a genuine takeover is a new <c>EventSource</c> with a new id.
/// </remarks>
public sealed class StreamOwnership
{
    /// <summary>
    /// How many displaced connection ids to remember.
    /// </summary>
    /// <remarks>
    /// Bounded because a long session reconnecting through bad wifi would otherwise accumulate one
    /// per drop for as long as it lasts. Sixteen is far more than the case this is for — two
    /// devices, taking it in turns — and an id that falls off the end costs one extra round trip
    /// rather than anything a player would notice.
    /// </remarks>
    private const int Remembered = 16;

    private readonly Lock _gate = new();
    private readonly Queue<string> _displaced = new();
    private CancellationTokenSource? _cancellation;
    private string? _current;

    /// <summary>The session whose stream is currently held, if any.</summary>
    public GameSession? Holder { get; private set; }

    /// <summary>Whether this connection has already been turned out.</summary>
    public bool IsSuperseded(string? connectionId)
    {
        if (string.IsNullOrEmpty(connectionId))
        {
            return false;
        }

        lock (_gate)
        {
            // Asked only of connections already displaced, never of unfamiliar ones. "Differs
            // from the newest" is the same sentence backwards and refuses every *arriving*
            // device instead of every replaced one - it refuses the takeover.
            return _displaced.Contains(connectionId, StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Takes sole possession of the stream, turning out whoever held it.
    /// </summary>
    /// <remarks>
    /// <b>The channel has exactly one reader, and this is what enforces it.</b> A session's
    /// <c>Events</c> channel is declared <c>SingleReader</c>, so two SSE responses draining it do
    /// not each get a copy of every event — they get roughly half each. That is not a race that
    /// has to be lost to be noticed; it is what a channel read twice does.
    /// </remarks>
    public StreamClaim Claim(GameSession session, string? connectionId)
    {
        lock (_gate)
        {
            Displace(connectionId);

            var cancellation = new CancellationTokenSource();
            _cancellation = cancellation;
            _current = connectionId;
            Holder = session;
            session.IsStreaming = true;

            return new StreamClaim(this, cancellation);
        }
    }

    /// <summary>
    /// Turns out the current stream because the character has been entered afresh.
    /// </summary>
    /// <remarks>
    /// Called from <see cref="SessionRegistry.Open"/>, and this is the beat that makes the
    /// hand-over read correctly: the older screen is told the moment the newer one *enters*,
    /// rather than discovering it when its channel goes quiet and racing to reconnect. Entering
    /// is the act that says "this device is playing this character now" — the stream that follows
    /// only carries out the decision.
    /// </remarks>
    public void DisplaceForNewSession()
    {
        lock (_gate)
        {
            Displace(incoming: null);
            Holder = null;
        }
    }

    /// <summary>Cancels the live claim and remembers the connection it belonged to.</summary>
    private void Displace(string? incoming)
    {
        _cancellation?.Cancel();

        if (Holder is { } previous)
        {
            previous.IsStreaming = false;
        }

        // Only a *different* connection is remembered. A screen resuming under the id it already
        // had is the same screen, and turning it out would lock a player out of their own
        // character after one dropped packet.
        if (_current is { } current
            && !string.Equals(current, incoming, StringComparison.Ordinal))
        {
            _displaced.Enqueue(current);

            while (_displaced.Count > Remembered)
            {
                _displaced.Dequeue();
            }
        }

        _current = null;
        _cancellation = null;
    }

    /// <summary>
    /// Gives the stream back, but only if this claim still holds it.
    /// </summary>
    /// <remarks>
    /// The reference check is the whole point. A displaced response disposes its claim *after* its
    /// successor has taken over, so an unconditional release would clear the live holder's
    /// cancellation and mark a streaming character idle.
    /// </remarks>
    internal void Release(CancellationTokenSource cancellation)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_cancellation, cancellation))
            {
                return;
            }

            if (Holder is { } holder)
            {
                holder.IsStreaming = false;
            }

            _cancellation = null;
            Holder = null;
        }
    }
}

/// <summary>
/// One SSE response's exclusive hold on a character's stream.
/// </summary>
/// <remarks>
/// <see cref="Displaced"/> is the question the response has to ask on the way out, because the two
/// ways a stream ends want opposite things. A connection that dropped is a link-dead character and
/// the grace window should start; a connection that was taken over is a character still very much
/// connected, on a different screen — telling the loop it went link-dead would narrate
/// <em>"goes still, eyes unfocused"</em> to the room about somebody standing right there.
/// </remarks>
public sealed class StreamClaim(StreamOwnership ownership, CancellationTokenSource cancellation)
    : IDisposable
{
    /// <summary>Cancelled when a newer stream, or a fresh entry, takes the character over.</summary>
    public CancellationToken Token => cancellation.Token;

    /// <summary>True once somebody else has taken the stream.</summary>
    public bool Displaced => cancellation.IsCancellationRequested;

    public void Dispose()
    {
        ownership.Release(cancellation);
        cancellation.Dispose();
    }
}

public enum SessionOpenOutcome
{
    Opened = 0,

    /// <summary>The same character was already connected; the previous stream was replaced.</summary>
    Replaced = 1,

    /// <summary>The account is already running its maximum number of characters.</summary>
    TooManyCharacters = 2,
}

public sealed record SessionOpenResult(SessionOpenOutcome Outcome, GameSession? Session);

public sealed class SessionRegistryOptions
{
    /// <summary>
    /// How many characters one account may have in the world simultaneously.
    /// </summary>
    /// <remarks>
    /// Multi-boxing is allowed by design, but not unbounded: each character holds an open SSE
    /// connection, a session, and a ring buffer, so an uncapped account could exhaust server
    /// resources by looping over its character list. Raise it in configuration if the game's
    /// policy wants more.
    /// </remarks>
    public int MaxConcurrentCharactersPerAccount { get; set; } = 3;

    /// <summary>
    /// How many characters one account may <b>have</b>, in the world or not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A roster limit, and a different question from
    /// <see cref="MaxConcurrentCharactersPerAccount"/> above — that one is a resource bound on how
    /// many may be <em>playing at once</em>, because each holds an open SSE connection and a ring
    /// buffer. This one bounds how many may exist at all, which costs a row rather than a socket.
    /// The two are easy to confuse and were: the shipped compose files set
    /// <c>Sessions__MaxCharactersPerAccount</c> for months against a server that had no such
    /// setting and no roster cap of any kind, so an account could create characters without limit
    /// while the deployment read as though it were capped at five.
    /// </para>
    /// <para>
    /// Counted over characters that have not been deleted, matching what the character list
    /// returns — a player who deleted one has the slot back, which is what deleting is for.
    /// </para>
    /// </remarks>
    public int MaxCharactersPerAccount { get; set; } = 8;

    /// <summary>
    /// How long a heartbeating client may go quiet before it is treated as gone, in seconds.
    /// </summary>
    /// <remarks>
    /// Comfortably more than the client's interval, because the cost of the two errors is not
    /// symmetric: reaping a live player who was briefly slow throws them out of the world, while
    /// waiting a few seconds longer on a client that has genuinely gone costs a session slot
    /// nobody is contending for. Three missed beats before anything happens.
    ///
    /// Zero or less disables the sweep entirely, which is the escape hatch if it ever misbehaves
    /// in a deployment: the server falls back to noticing dropped connections the way it always
    /// has, which is to say slowly.
    /// </remarks>
    public int HeartbeatTimeoutSeconds { get; set; } = 60;
}

/// <summary>
/// Sessions keyed by <b>character</b>, not by account, so one account can play several
/// characters at once - each with its own stream, scrollback, and link-dead window.
/// </summary>
/// <remarks>
/// Keying by account was the earlier design and made entering a second character silently
/// evict the first. Because the routes are character-scoped, the cookie alone is no longer
/// enough to identify a session, so every lookup also verifies the character belongs to the
/// calling account.
/// </remarks>
public sealed class SessionRegistry(SessionRegistryOptions options)
{
    private readonly ConcurrentDictionary<Guid, GameSession> _byCharacter = new();

    /// <summary>
    /// Who may stream each character, kept apart from the sessions themselves.
    /// </summary>
    /// <remarks>
    /// Keyed by character and deliberately outliving <see cref="Open"/>, which builds a new
    /// session every time. Ownership held on a session was wiped precisely when a second device
    /// entered, so the older device's retry found a session that had never heard of it, was
    /// served, and took the character back off the device that had just claimed it. That is the
    /// ping-pong, and this dictionary is where it stops.
    /// </remarks>
    private readonly ConcurrentDictionary<Guid, StreamOwnership> _streams = new();

    public int Count => _byCharacter.Count;

    /// <summary>Who may stream this character. Created on first ask.</summary>
    public StreamOwnership StreamFor(Guid characterId) =>
        _streams.GetOrAdd(characterId, _ => new StreamOwnership());

    public SessionOpenResult Open(Guid accountId, Guid characterId, string characterName)
    {
        var existing = _byCharacter.GetValueOrDefault(characterId);

        // Re-entering the same character is a reconnect, not a new presence, so it does not
        // count against the cap. The Engine rebinds the actor rather than cloning it.
        if (existing is null && CountFor(accountId) >= options.MaxConcurrentCharactersPerAccount)
        {
            return new SessionOpenResult(SessionOpenOutcome.TooManyCharacters, null);
        }

        var session = new GameSession
        {
            AccountId = accountId,
            CharacterId = characterId,
            CharacterName = characterName,
        };

        var replaced = false;

        _byCharacter.AddOrUpdate(characterId, session, (_, previous) =>
        {
            // Same character opened from a second tab: drop the older stream. One character,
            // one connection - otherwise both tabs would show half the output each.
            previous.Events.Writer.TryComplete();
            replaced = true;
            return session;
        });

        if (replaced)
        {
            // Turn the older screen out *here*, at the moment somebody enters, rather than
            // leaving it to notice its channel has gone quiet. Entering is the act that decides
            // which device is playing this character; the stream only carries the decision out.
            // Left to the stream, the older device sees its connection die, cannot tell that from
            // a network drop, and reconnects - which is the tug-of-war this whole mechanism is
            // for. Its response is cancelled and says so before the new device even connects.
            StreamFor(characterId).DisplaceForNewSession();
        }

        return new SessionOpenResult(
            replaced ? SessionOpenOutcome.Replaced : SessionOpenOutcome.Opened,
            session);
    }

    /// <summary>
    /// Finds a session, refusing to return one belonging to a different account even when the
    /// character id is correct.
    /// </summary>
    public GameSession? Find(Guid accountId, Guid characterId)
    {
        var session = _byCharacter.GetValueOrDefault(characterId);
        return session?.AccountId == accountId ? session : null;
    }

    /// <summary>Every live session, for the sweep that reaps the ones that have gone quiet.</summary>
    public IReadOnlyList<GameSession> All => [.. _byCharacter.Values];

    public IReadOnlyList<GameSession> ForAccount(Guid accountId) =>
        [.. _byCharacter.Values.Where(s => s.AccountId == accountId)];

    public void Close(Guid characterId)
    {
        if (_byCharacter.TryRemove(characterId, out var session))
        {
            session.Events.Writer.TryComplete();
        }

        // The character has left the world, so its stream history goes with it. Keeping it would
        // mean a player who left and came back on the same device could be turned away by a
        // decision made about a session that no longer exists.
        _streams.TryRemove(characterId, out _);
    }

    private int CountFor(Guid accountId) =>
        _byCharacter.Values.Count(s => s.AccountId == accountId);
}
