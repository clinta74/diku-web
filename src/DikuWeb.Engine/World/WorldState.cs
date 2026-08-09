using DikuWeb.Domain.Abilities.Effects;
using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Items;
using DikuWeb.Domain.Quests;
using DikuWeb.Domain.Randomness;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Abilities;
using DikuWeb.Engine.Social;

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
    private readonly Dictionary<Guid, HashSet<Guid>> _mobsBySpawner = [];
    private readonly Dictionary<Guid, ItemInstance> _items = [];
    private readonly Dictionary<RoomKey, List<ItemInstance>> _itemsByRoom = [];
    private readonly Dictionary<RoomKey, Combat> _combatsByRoom = [];
    private readonly CastQueueService _castQueue = new();
    private readonly Dictionary<(Guid CharacterId, string AbilityKey), long> _abilityCooldowns = [];
    private readonly Dictionary<Guid, List<ActiveEffect>> _activeEffects = [];
    private readonly Dictionary<Guid, Dictionary<string, CharacterQuest>> _questsByCharacter = [];
    private readonly PartyRegistry _parties = new();

    public IRandomSource Random => _random;

    /// <summary>
    /// Who is grouped with whom (PLAN.md §5.3). Lives here rather than in a table because a
    /// party is session-scoped: it describes the world as it stands, like combat and active
    /// effects do, and it ends with the session rather than outliving it.
    /// </summary>
    public PartyRegistry Parties => _parties;

    public CastQueueService CastQueue => _castQueue;

    /// <summary>Get the last pulse when an ability was cast (for cooldown checking).</summary>
    /// <summary>
    /// The pulse an ability was last cast on, or null if this character has never cast it.
    /// </summary>
    /// <remarks>
    /// Null rather than 0. Zero is a real pulse - the one the server starts on - so returning it
    /// for "never cast" made every ability read as freshly used at boot, and the whole spellbook
    /// was refused for its own cooldown duration after every restart. It also made a cast at
    /// pulse 0 untestable, which is why nothing had noticed.
    /// </remarks>
    public long? GetAbilityCooldown(Guid characterId, string abilityKey) =>
        _abilityCooldowns.TryGetValue((characterId, abilityKey), out var lastPulse)
            ? lastPulse
            : null;

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

    /// <summary>
    /// Takes a character out of the world. The one door out, which is why the party registry is
    /// cleaned up here rather than at each of the four call sites that lead to it.
    /// </summary>
    public void Remove(PlayerActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);

        _byCharacter.Remove(actor.CharacterId);
        _bySession.Remove(actor.SessionId);
        OccupantList(actor.RoomKey).Remove(actor);
        _parties.Forget(actor.CharacterId);
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

        if (mob.SpawnerId is { } spawnerId)
        {
            SpawnedListFor(spawnerId).Add(mob.Id);
        }
    }

    /// <summary>Removes a mob from the world.</summary>
    public void RemoveMob(Mob mob)
    {
        ArgumentNullException.ThrowIfNull(mob);

        var roomKey = RoomKey.Parse(mob.RoomKey);
        _mobs.Remove(mob.Id);
        MobListFor(roomKey).Remove(mob);

        if (mob.SpawnerId is { } spawnerId && _mobsBySpawner.TryGetValue(spawnerId, out var spawned))
        {
            spawned.Remove(mob.Id);

            if (spawned.Count == 0)
            {
                _mobsBySpawner.Remove(spawnerId);
            }
        }
    }

    /// <summary>
    /// How many living mobs this spawner is responsible for, anywhere in the world.
    /// </summary>
    /// <remarks>
    /// <b>Anywhere</b>, not in the spawner's own rooms. The sweep used to count what was standing
    /// in the rooms it fills, so a rat that wandered one room east stopped counting and the
    /// spawner replaced it - and the replacement wandered off too. A spawner set to three could
    /// populate a whole zone with rats given long enough, which is the flooding this fixes.
    ///
    /// Indexed rather than scanned, because the alternative is every spawner walking every mob in
    /// the world on every sweep - quadratic in exactly the situation the bug produced.
    /// </remarks>
    public int MobsFromSpawner(Guid spawnerId) =>
        _mobsBySpawner.TryGetValue(spawnerId, out var spawned) ? spawned.Count : 0;

    private HashSet<Guid> SpawnedListFor(Guid spawnerId)
    {
        if (!_mobsBySpawner.TryGetValue(spawnerId, out var spawned))
        {
            spawned = [];
            _mobsBySpawner[spawnerId] = spawned;
        }

        return spawned;
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

    /// <summary>Get all active effects on an entity.</summary>
    public IReadOnlyList<ActiveEffect> GetActiveEffects(Guid entityId) =>
        _activeEffects.TryGetValue(entityId, out var list) ? list : [];

    /// <summary>
    /// Whether this entity is stunned and so takes no turn at all.
    /// </summary>
    /// <remarks>
    /// Asked in three places - the combat loop, the cast command, and the mob AI - and each one
    /// answering for itself is how a stun ends up stopping swings but not spells. The expiry is
    /// checked here rather than trusted to the sweep, which runs on the 60s tick: a stun measured
    /// in a couple of swings would otherwise outlive itself by most of a minute.
    /// </remarks>
    public bool IsStunned(Guid entityId, long currentPulse) =>
        _activeEffects.TryGetValue(entityId, out var list) &&
        list.Any(e => e.PreventsActing && e.ExpiresAtPulse > currentPulse);

    /// <summary>
    /// Whether this entity is snared and so cannot leave the room or flee a fight.
    /// </summary>
    /// <remarks>
    /// Expiry is checked here for the same reason as <see cref="IsStunned"/>: the sweep that
    /// removes effects runs on the 60s tick, and a snare measured in seconds would otherwise
    /// hold long after it was supposed to let go.
    /// </remarks>
    public bool IsRooted(Guid entityId, long currentPulse) =>
        _activeEffects.TryGetValue(entityId, out var list) &&
        list.Any(e => e.PreventsEscape && e.ExpiresAtPulse > currentPulse);

    /// <summary>The name of whatever is holding this entity, for narration.</summary>
    public string? RootName(Guid entityId, long currentPulse) =>
        _activeEffects.TryGetValue(entityId, out var list)
            ? list.FirstOrDefault(e => e.PreventsEscape && e.ExpiresAtPulse > currentPulse)?.Name
            : null;

    /// <summary>Apply a new effect to an entity, handling stacking rules.</summary>
    public void ApplyEffect(Guid entityId, ActiveEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);

        if (!_activeEffects.TryGetValue(entityId, out var effects))
        {
            effects = [];
            _activeEffects[entityId] = effects;
        }

        // Find existing effect by key and source
        var existing = effects.FirstOrDefault(e => e.EffectKey == effect.EffectKey && e.SourceEntityId == effect.SourceEntityId);

        if (existing is null)
        {
            // No existing effect, add new one
            effects.Add(effect);
        }
        else
        {
            // Existing effect found, apply stacking rule
            switch (existing.StackingRule)
            {
                case EffectStackingRule.Refresh:
                    // Just reset expiry, keep everything else
                    existing.ExpiresAtPulse = effect.ExpiresAtPulse;
                    break;

                case EffectStackingRule.Stack:
                    // Add stack if below max, update expiry
                    if (existing.Stacks < existing.MaxStacks)
                    {
                        existing.Stacks++;
                    }
                    existing.ExpiresAtPulse = effect.ExpiresAtPulse;
                    break;

                case EffectStackingRule.Ignore:
                    // Do nothing, existing effect continues unchanged
                    break;
            }
        }
    }

    /// <summary>Remove and return all expired effects across all entities.</summary>
    public IReadOnlyList<ActiveEffect> ExpireEffects(long currentPulse)
    {
        var expired = new List<ActiveEffect>();

        foreach (var entityId in _activeEffects.Keys.ToList())
        {
            var effects = _activeEffects[entityId];
            var toRemove = effects.Where(e => e.ExpiresAtPulse <= currentPulse).ToList();

            foreach (var effect in toRemove)
            {
                effects.Remove(effect);
                expired.Add(effect);
            }

            // Clean up empty lists
            if (effects.Count == 0)
            {
                _activeEffects.Remove(entityId);
            }
        }

        return expired;
    }

    /// <summary>Get a character by ID (format: c_<guid>).</summary>
    public Character? GetCharacter(Guid characterId) => FindByCharacter(characterId)?.Character;

    /// <summary>Get a mob by ID.</summary>
    public Mob? GetMob(Guid mobId) => FindMob(mobId);

    /// <summary>Load quests for a character from the repository result (called on EnterWorld).</summary>
    public void LoadCharacterQuests(Guid characterId, IEnumerable<CharacterQuest> quests)
    {
        if (!_questsByCharacter.TryGetValue(characterId, out var questDict))
        {
            questDict = [];
            _questsByCharacter[characterId] = questDict;
        }

        foreach (var quest in quests)
        {
            questDict[quest.QuestKey] = quest;
        }
    }

    /// <summary>Load a character's inventory and equipped items from the database.</summary>
    public void LoadCharacterItems(Guid characterId, IEnumerable<ItemInstance> items)
    {
        foreach (var item in items)
        {
            _items[item.Id] = item;
        }
    }

    /// <summary>Get all quests for a character, grouped by status.</summary>
    public IReadOnlyList<CharacterQuest> QuestsFor(Guid characterId) =>
        _questsByCharacter.TryGetValue(characterId, out var quests) ? quests.Values.ToList() : [];

    /// <summary>Get the state of a specific quest for a character, or null if not started.</summary>
    public CharacterQuest? GetQuestState(Guid characterId, string questKey)
    {
        if (_questsByCharacter.TryGetValue(characterId, out var quests))
        {
            quests.TryGetValue(questKey, out var quest);
            return quest;
        }
        return null;
    }

    /// <summary>Set or update the state of a quest for a character.</summary>
    public void SetQuestState(Guid characterId, string questKey, CharacterQuest quest)
    {
        ArgumentNullException.ThrowIfNull(quest);

        if (!_questsByCharacter.TryGetValue(characterId, out var quests))
        {
            quests = [];
            _questsByCharacter[characterId] = quests;
        }

        quests[questKey] = quest;
    }
}
