using DikuWeb.Domain.Worlds;

namespace DikuWeb.Engine.World;

/// <summary>
/// The authoritative in-memory world. Owned by the single game-loop thread and deliberately
/// NOT thread-safe (PLAN.md §2.1) - adding a lock here would be a sign something is calling
/// it from the wrong place.
/// </summary>
public sealed class WorldState
{
    private readonly Dictionary<string, Domain.Worlds.World> _worlds = [];
    private readonly Dictionary<string, Zone> _zones = [];
    private readonly Dictionary<RoomKey, Room> _rooms = [];
    private readonly Dictionary<RoomKey, List<PlayerActor>> _occupants = [];
    private readonly Dictionary<Guid, PlayerActor> _bySession = [];
    private readonly Dictionary<Guid, PlayerActor> _byCharacter = [];

    public int RoomCount => _rooms.Count;

    public int PlayerCount => _byCharacter.Count;

    public IEnumerable<PlayerActor> AllPlayers => _byCharacter.Values;

    public void Load(
        IEnumerable<Domain.Worlds.World> worlds,
        IEnumerable<Zone> zones,
        IEnumerable<Room> rooms)
    {
        ArgumentNullException.ThrowIfNull(worlds);
        ArgumentNullException.ThrowIfNull(zones);
        ArgumentNullException.ThrowIfNull(rooms);

        _worlds.Clear();
        _zones.Clear();
        _rooms.Clear();

        foreach (var world in worlds)
        {
            _worlds[world.Key] = world;
        }

        foreach (var zone in zones)
        {
            _zones[zone.Key] = zone;
        }

        foreach (var room in rooms)
        {
            _rooms[room.Key] = room;
        }
    }

    public bool TryGetRoom(RoomKey key, out Room room) => _rooms.TryGetValue(key, out room!);

    // -----------------------------------------------------------------------
    // Content mutation (PLAN.md §7.3). Called only from WorldMutationApplier,
    // which the game loop calls on its own thread.
    // -----------------------------------------------------------------------

    public void PutWorld(Domain.Worlds.World world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _worlds[world.Key] = world;
    }

    public bool RemoveWorld(string key) => _worlds.Remove(key);

    public void PutZone(Zone zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        _zones[zone.Key] = zone;
    }

    public bool RemoveZone(string key) => _zones.Remove(key);

    public void PutRoom(Room room)
    {
        ArgumentNullException.ThrowIfNull(room);
        _rooms[room.Key] = room;
    }

    /// <summary>
    /// Removes the room and the exits out of it. Exits pointing <em>at</em> it are left alone
    /// and become dangling links, which movement fails closed on (PLAN.md §7.4).
    /// </summary>
    public bool RemoveRoom(RoomKey key)
    {
        _occupants.Remove(key);
        return _rooms.Remove(key);
    }

    /// <summary>Every exit anywhere in the world that points at this room.</summary>
    public IEnumerable<RoomExit> ExitsPointingAt(RoomKey key) =>
        _rooms.Values.SelectMany(r => r.Exits).Where(e => e.ToRoomKey == key);

    public Room? FindRoom(RoomKey key) => _rooms.GetValueOrDefault(key);

    public Zone? FindZone(string zoneKey) => _zones.GetValueOrDefault(zoneKey);

    public Domain.Worlds.World? FindWorld(string worldKey) => _worlds.GetValueOrDefault(worldKey);

    public IEnumerable<Domain.Worlds.World> AllWorlds => _worlds.Values;

    public IEnumerable<Zone> AllZones => _zones.Values;

    public IEnumerable<Room> AllRooms => _rooms.Values;

    public IEnumerable<Zone> ZonesIn(string worldKey) =>
        _zones.Values.Where(z => string.Equals(z.WorldKey, worldKey, StringComparison.Ordinal));

    public IEnumerable<Room> RoomsIn(string zoneKey) =>
        _rooms.Values.Where(r => string.Equals(r.ZoneKey, zoneKey, StringComparison.Ordinal));

    /// <summary>
    /// Resolves a flag down room → zone → world → registry default (PLAN.md §4.10). The
    /// lookups are done here rather than through navigation properties because the world is
    /// loaded AsNoTracking, so <see cref="Room.Zone"/> is null in the running engine.
    /// </summary>
    public FlagResolution ResolveFlag(Room room, RoomFlag flag)
    {
        ArgumentNullException.ThrowIfNull(room);

        var zone = FindZone(room.ZoneKey);
        var world = zone is null ? null : FindWorld(zone.WorldKey);

        return RoomFlags.Resolve(flag, room.Flags, zone?.Flags, world?.Flags);
    }

    /// <summary>
    /// True when the flag resolves on. A room that does not exist resolves to the registry
    /// default, which is always the safe value - so a deleted room never becomes PvP.
    /// </summary>
    public bool IsFlagSet(RoomKey key, RoomFlag flag)
    {
        ArgumentNullException.ThrowIfNull(flag);

        var room = FindRoom(key);
        return room is null ? flag.Default : ResolveFlag(room, flag).Value;
    }

    public PlayerActor? FindBySession(Guid sessionId) => _bySession.GetValueOrDefault(sessionId);

    public PlayerActor? FindByCharacter(Guid characterId) =>
        _byCharacter.GetValueOrDefault(characterId);

    /// <summary>Case-insensitive, so "kael" finds Kael - matching how players actually type.</summary>
    public PlayerActor? FindPlayerByName(string name) =>
        _byCharacter.Values.FirstOrDefault(
            p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<PlayerActor> OccupantsOf(RoomKey key) =>
        _occupants.TryGetValue(key, out var list) ? list : [];

    /// <summary>Everyone in the room except the given actor - the usual audience for a message.</summary>
    public IEnumerable<PlayerActor> OthersIn(RoomKey key, PlayerActor except) =>
        OccupantsOf(key).Where(p => p.CharacterId != except.CharacterId);

    public void Add(PlayerActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);

        _byCharacter[actor.CharacterId] = actor;
        _bySession[actor.SessionId] = actor;
        OccupantList(actor.RoomKey).Add(actor);
    }

    public void Remove(PlayerActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);

        _byCharacter.Remove(actor.CharacterId);
        _bySession.Remove(actor.SessionId);
        OccupantList(actor.RoomKey).Remove(actor);
    }

    /// <summary>Rebinds an actor to a new session after a reconnect.</summary>
    public void Rebind(PlayerActor actor, Guid newSessionId)
    {
        ArgumentNullException.ThrowIfNull(actor);

        _bySession.Remove(actor.SessionId);
        actor.SessionId = newSessionId;
        _bySession[newSessionId] = actor;
    }

    public void Move(PlayerActor actor, RoomKey destination)
    {
        ArgumentNullException.ThrowIfNull(actor);

        OccupantList(actor.RoomKey).Remove(actor);
        actor.Character.RoomKey = destination;
        OccupantList(destination).Add(actor);
    }

    private List<PlayerActor> OccupantList(RoomKey key)
    {
        if (!_occupants.TryGetValue(key, out var list))
        {
            list = [];
            _occupants[key] = list;
        }

        return list;
    }
}
