using System.Collections.Concurrent;
using System.Threading.Channels;
using DikuWeb.Engine.Protocol;

namespace DikuWeb.Server.Game;

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

    /// <summary>True while an SSE response is actively draining this session.</summary>
    public bool IsStreaming { get; set; }

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

    public int Count => _byCharacter.Count;

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

    public IReadOnlyList<GameSession> ForAccount(Guid accountId) =>
        [.. _byCharacter.Values.Where(s => s.AccountId == accountId)];

    public void Close(Guid characterId)
    {
        if (_byCharacter.TryRemove(characterId, out var session))
        {
            session.Events.Writer.TryComplete();
        }
    }

    private int CountFor(Guid accountId) =>
        _byCharacter.Values.Count(s => s.AccountId == accountId);
}
