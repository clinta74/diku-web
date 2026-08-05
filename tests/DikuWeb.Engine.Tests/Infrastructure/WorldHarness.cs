using System.Threading.Channels;
using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Commands;
using DikuWeb.Engine.Presentation;
using DikuWeb.Engine.Protocol;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Tests.Infrastructure;

/// <summary>
/// Wires the real WorldState, CommandRegistry, and PlayerView without the hosted service, so
/// command behaviour can be asserted synchronously. No timers, no sleeping, no database.
/// </summary>
internal sealed class WorldHarness
{
    private readonly Dictionary<Guid, Channel<OutboundEvent>> _channels = [];

    public WorldHarness()
    {
        World = new WorldState();
        Commands = new CommandRegistry();
        View = new PlayerView(new RoomLayoutService());
    }

    public WorldState World { get; }

    public CommandRegistry Commands { get; }

    public PlayerView View { get; }

    /// <summary>
    /// Three rooms west-to-east, plus a fourth exit off the east room that points at a room
    /// which does not exist - the dangling-exit case live editing makes routine.
    /// </summary>
    public static (Room West, Room Middle, Room East) BuildTestRooms()
    {
        var west = NewRoom(
            "west",
            grid: ["#######", "#.....#", "#.....#", "#######"],
            legend: new() { ["#"] = "wall", ["."] = "floor" });
        var middle = NewRoom("middle");
        var east = NewRoom("east");

        Link(west, Direction.East, middle);
        Link(middle, Direction.East, east);

        east.Exits.Add(new RoomExit
        {
            FromRoomKey = east.Key,
            Direction = Direction.North,
            ToRoomKey = RoomKey.Parse("test.zone.nowhere"),
        });

        return (west, middle, east);
    }

    public void LoadTestWorld()
    {
        var (west, middle, east) = BuildTestRooms();
        World.Load([], [], [west, middle, east]);
    }

    public static Room NewRoom(
        string slug,
        string[]? grid = null,
        Dictionary<string, string>? legend = null) =>
        new()
        {
            Key = RoomKey.Create("test", "zone", slug),
            ZoneKey = "test.zone",
            Title = $"The {slug} room",
            Description = $"A featureless {slug} room used for testing.",
            Grid = [.. grid ?? []],
            Legend = legend ?? [],
        };

    public static void Link(Room from, Direction direction, Room to)
    {
        from.Exits.Add(new RoomExit
        {
            FromRoomKey = from.Key,
            Direction = direction,
            ToRoomKey = to.Key,
        });

        to.Exits.Add(new RoomExit
        {
            FromRoomKey = to.Key,
            Direction = direction.Opposite(),
            ToRoomKey = from.Key,
        });
    }

    public PlayerActor AddPlayer(string name, RoomKey at)
    {
        var channel = Channel.CreateUnbounded<OutboundEvent>();

        var actor = new PlayerActor
        {
            Character = NewCharacter(name, at),
            SessionId = Guid.CreateVersion7(),
            Output = channel.Writer,
        };

        _channels[actor.CharacterId] = channel;
        World.Add(actor);
        return actor;
    }

    public static Character NewCharacter(string name, RoomKey at) => new()
    {
        AccountId = Guid.CreateVersion7(),
        Name = name,
        Path = CharacterPath.Warden,
        Attributes = AttributeSet.Baseline,
        Vitals = Vitals.StartingFor(CharacterPath.Warden),
        RoomKey = at,
        CreatedAt = DateTimeOffset.UnixEpoch,
    };

    /// <summary>Runs a command exactly as the game loop would, minus the loop.</summary>
    public CommandContext Execute(PlayerActor actor, string input)
    {
        var (verb, argument) = CommandRegistry.Split(input);
        var definition = Commands.Find(verb)
            ?? throw new InvalidOperationException($"No command matched '{verb}'.");

        var context = new CommandContext
        {
            Actor = actor,
            World = World,
            View = View,
            Verb = verb,
            Argument = argument,
        };

        definition.Handler(context);
        return context;
    }

    /// <summary>Everything queued for this player since the last drain.</summary>
    public List<OutboundEvent> Drain(PlayerActor actor)
    {
        var events = new List<OutboundEvent>();
        var reader = _channels[actor.CharacterId].Reader;

        while (reader.TryRead(out var gameEvent))
        {
            events.Add(gameEvent);
        }

        return events;
    }

    /// <summary>All text produced for this player, flattened into one string.</summary>
    public string DrainText(PlayerActor actor) =>
        string.Concat(Drain(actor)
            .Where(e => e.Type == EventTypes.Text)
            .Cast<OutboundEvent>()
            .Select(e => string.Concat(((TextPayload)e.Payload).Spans.Select(s => s.T))));
}
