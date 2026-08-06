using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Items;
using DikuWeb.Domain.Randomness;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Abilities;

namespace DikuWeb.Engine.World;

/// <summary>
/// The authoritative in-memory world. Owned by the single game-loop thread and deliberately
/// NOT thread-safe (PLAN.md §2.1) - adding a lock here would be a sign something is calling
/// it from the wrong place.
/// </summary>
public sealed class WorldState(IRandomSource random)
{
    private readonly IRandomSource _random = random;
    private readonly Dictionary<string, Domain.Worlds.World> _worlds = [];
    private readonly Dictionary<string, Zone> _zones = [];
    private readonly Dictionary<RoomKey, Room> _rooms = [];
    private readonly Dictionary<RoomKey, List<PlayerActor>> _occupants = [];
    private readonly Dictionary<Guid, PlayerActor> _bySession = [];
    private readonly Dictionary<Guid, PlayerActor> _byCharacter = [];
    private readonly Dictionary<Guid, Mob> _mobs = [];
    private readonly Dictionary<RoomKey, List<Mob>> _mobsByRoom = [];
    private readonly Dictionary<Guid, ItemInstance> _items = [];
    private readonly Dictionary<RoomKey, List<ItemInstance>> _itemsByRoom = [];
    private readonly Dictionary<RoomKey, Combat> _combatsByRoom = [];
    private readonly CastQueueService _castQueue = new();
    private readonly Dictionary<(Guid CharacterId, string AbilityKey), long> _abilityCooldowns = [];

    public IRandomSource Random => _random;

    public CastQueueService CastQueue => _castQueue;

    /// <summary>Get the last pulse when an ability was cast (for cooldown checking).</summary>
    public long GetAbilityCooldown(Guid characterId, string abilityKey)
    {
        var key = (characterId, abilityKey);
        _abilityCooldowns.TryGetValue(key, out var lastPulse);
        return lastPulse;
    }

    /// <summary>Set the last pulse when an ability was cast.</summary>
    public void SetAbilityCooldown(Guid characterId, string abilityKey, long pulse)
    {
        var key = (characterId, abilityKey);
        _abilityCooldowns[key] = pulse;
    }

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

    /// <summary>Adds a mob to a room.</summary>
    public void AddMob(Mob mob)
    {
        ArgumentNullException.ThrowIfNull(mob);

        var roomKey = RoomKey.Parse(mob.RoomKey);
        _mobs[mob.Id] = mob;
        MobListFor(roomKey).Add(mob);
    }

    /// <summary>Removes a mob from the world.</summary>
    public void RemoveMob(Mob mob)
    {
        ArgumentNullException.ThrowIfNull(mob);

        var roomKey = RoomKey.Parse(mob.RoomKey);
        _mobs.Remove(mob.Id);
        MobListFor(roomKey).Remove(mob);
    }

    /// <summary>Moves a mob to a new room.</summary>
    public void MoveMob(Mob mob, RoomKey destination)
    {
        ArgumentNullException.ThrowIfNull(mob);

        var current = RoomKey.Parse(mob.RoomKey);
        MobListFor(current).Remove(mob);
        mob.RoomKey = destination.ToString();
        MobListFor(destination).Add(mob);
    }

    /// <summary>All mobs in a specific room.</summary>
    public IReadOnlyList<Mob> MobsIn(RoomKey key) =>
        _mobsByRoom.TryGetValue(key, out var list) ? list : [];

    /// <summary>All mobs currently in the world.</summary>
    public IEnumerable<Mob> AllMobs => _mobs.Values;

    public Mob? FindMob(Guid mobId) => _mobs.GetValueOrDefault(mobId);

    private List<Mob> MobListFor(RoomKey key)
    {
        if (!_mobsByRoom.TryGetValue(key, out var list))
        {
            list = [];
            _mobsByRoom[key] = list;
        }

        return list;
    }

    /// <summary>Adds an item instance to a room.</summary>
    public void AddItem(ItemInstance item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.RoomKey is not null)
        {
            var roomKey = RoomKey.Parse(item.RoomKey);
            _items[item.Id] = item;
            ItemListFor(roomKey).Add(item);
        }
    }

    /// <summary>Removes an item from the world.</summary>
    public void RemoveItem(ItemInstance item)
    {
        ArgumentNullException.ThrowIfNull(item);

        _items.Remove(item.Id);
        if (item.RoomKey is not null)
        {
            var roomKey = RoomKey.Parse(item.RoomKey);
            ItemListFor(roomKey).Remove(item);
        }
    }

    /// <summary>All items in a specific room.</summary>
    public IReadOnlyList<ItemInstance> ItemsIn(RoomKey key) =>
        _itemsByRoom.TryGetValue(key, out var list) ? list : [];

    /// <summary>All items currently in the world.</summary>
    public IEnumerable<ItemInstance> AllItems => _items.Values;

    public ItemInstance? FindItem(Guid itemId) => _items.GetValueOrDefault(itemId);

    /// <summary>All items in a character's inventory.</summary>
    public IReadOnlyList<ItemInstance> InventoryOf(Guid characterId) =>
        _items.Values.Where(i => i.OwnerCharacterId == characterId).ToList();

    /// <summary>All items equipped on a character.</summary>
    public IReadOnlyList<ItemInstance> EquipmentOf(Guid characterId) =>
        _items.Values.Where(i => i.OwnerCharacterId == characterId && i.EquippedSlot != null).ToList();

    /// <summary>Moves an item to a character's inventory.</summary>
    public void PickUpItem(ItemInstance item, Guid characterId)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.RoomKey is not null)
        {
            var roomKey = RoomKey.Parse(item.RoomKey);
            ItemListFor(roomKey).Remove(item);
        }

        item.RoomKey = null;
        item.OwnerCharacterId = characterId;
    }

    /// <summary>Moves an item to the ground in a room.</summary>
    public void DropItem(ItemInstance item, RoomKey room)
    {
        ArgumentNullException.ThrowIfNull(item);

        item.OwnerCharacterId = null;
        item.RoomKey = room.ToString();
        ItemListFor(room).Add(item);
    }

    /// <summary>Equips an item on a character.</summary>
    public void EquipItem(ItemInstance item, ItemSlot slot)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.EquippedSlot = slot;
    }

    /// <summary>Unequips an item from a character.</summary>
    public void UnequipItem(ItemInstance item)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.EquippedSlot = null;
    }

    private List<ItemInstance> ItemListFor(RoomKey key)
    {
        if (!_itemsByRoom.TryGetValue(key, out var list))
        {
            list = [];
            _itemsByRoom[key] = list;
        }

        return list;
    }

    /// <summary>Get or create a combat instance for a room.</summary>
    public Combat GetOrCreateCombat(RoomKey roomKey)
    {
        if (!_combatsByRoom.TryGetValue(roomKey, out var combat))
        {
            combat = new Combat { RoomKey = roomKey };
            _combatsByRoom[roomKey] = combat;
        }
        return combat;
    }

    /// <summary>Find an existing combat in a room, or null if none.</summary>
    public Combat? FindCombat(RoomKey roomKey) =>
        _combatsByRoom.TryGetValue(roomKey, out var combat) ? combat : null;

    /// <summary>All active combats in the world.</summary>
    public IEnumerable<Combat> AllCombats => _combatsByRoom.Values;

    /// <summary>Remove a combat (when it ends).</summary>
    public void EndCombat(RoomKey roomKey) => _combatsByRoom.Remove(roomKey);

    /// <summary>Get a character by ID (format: c_<guid>).</summary>
    public Character? GetCharacter(Guid characterId) => FindByCharacter(characterId)?.Character;

    /// <summary>Get a mob by ID.</summary>
    public Mob? GetMob(Guid mobId) => FindMob(mobId);
}
