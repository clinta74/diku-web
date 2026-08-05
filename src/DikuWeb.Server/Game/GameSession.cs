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

/// <summary>
/// Sessions keyed by account, so the SSE endpoint never needs a session id in the URL -
/// the auth cookie is the only credential in play (PLAN.md §3.2).
/// </summary>
public sealed class SessionRegistry
{
    private readonly ConcurrentDictionary<Guid, GameSession> _byAccount = new();

    public GameSession Open(Guid accountId, Guid characterId, string characterName)
    {
        var session = new GameSession
        {
            AccountId = accountId,
            CharacterId = characterId,
            CharacterName = characterName,
        };

        // Replacing an existing session drops the old stream, which is the correct outcome
        // when the same account enters the world again from a second tab.
        _byAccount.AddOrUpdate(accountId, session, (_, previous) =>
        {
            previous.Events.Writer.TryComplete();
            return session;
        });

        return session;
    }

    public GameSession? Find(Guid accountId) => _byAccount.GetValueOrDefault(accountId);

    public void Close(Guid accountId)
    {
        if (_byAccount.TryRemove(accountId, out var session))
        {
            session.Events.Writer.TryComplete();
        }
    }

    public int Count => _byAccount.Count;
}
