using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Items;
using DikuWeb.Domain.Spawning;
using DikuWeb.Domain.Worlds;

namespace DikuWeb.Engine.Mutations;

/// <summary>
/// A world-content edit (PLAN.md §7.3). Builder endpoints turn a request into one of these and
/// hand it to the game loop, which is the only thing allowed to mutate world state (§2.1).
/// </summary>
/// <remarks>
/// Split into <em>requests</em>, which are what a builder asks for, and <em>primitives</em>,
/// which are what actually happened. The loop normalises the former into an ordered list of the
/// latter, applies them to memory, and hands the same list to persistence - so the database
/// replays exactly the operations memory performed, in the same order, rather than the two
/// sides independently interpreting the request and drifting apart.
///
/// The clearest case is <see cref="DigRoom"/>: the loop picks the new room's key, so only a
/// normalised list can tell persistence which key to write.
/// </remarks>
public abstract record WorldChange
{
    /// <summary>For the audit row: "world", "zone", "room", or "exit".</summary>
    public abstract string EntityKind { get; }

    public abstract string EntityKey { get; }
}

// ---------------------------------------------------------------------------
// Primitives - applied to memory and replayed into the database verbatim
// ---------------------------------------------------------------------------

public sealed record UpsertWorld(
    string Key,
    string Name,
    string Description,
    int SortOrder,
    FlagSet Flags,
    Multipliers Multipliers) : WorldChange
{
    public override string EntityKind => "world";

    public override string EntityKey => Key;
}

public sealed record DeleteWorld(string Key) : WorldChange
{
    public override string EntityKind => "world";

    public override string EntityKey => Key;
}

public sealed record UpsertZone(
    string Key,
    string WorldKey,
    string Name,
    string Description,
    int MinLevel,
    int MaxLevel,
    FlagSet Flags,
    Multipliers Multipliers) : WorldChange
{
    public override string EntityKind => "zone";

    public override string EntityKey => Key;
}

public sealed record DeleteZone(string Key) : WorldChange
{
    public override string EntityKind => "zone";

    public override string EntityKey => Key;
}

/// <summary>
/// Creates or replaces a room's own fields. Deliberately does not carry exits: those are
/// separate primitives, so editing a description can never silently rewrite the exit graph.
/// </summary>
public sealed record UpsertRoom(
    RoomKey Key,
    string ZoneKey,
    string Title,
    string Description,
    FlagSet Flags,
    IReadOnlyList<string> Grid,
    IReadOnlyDictionary<string, string> Legend,
    int? EditorX,
    int? EditorY) : WorldChange
{
    public override string EntityKind => "room";

    public override string EntityKey => Key.ToString();
}

/// <summary>
/// Removes a room and the exits leading out of it. Exits pointing <em>at</em> it are left
/// dangling on purpose - that is the state §7.4 makes the world tolerate, and rewriting other
/// rooms' exits as a side effect of a delete would be a surprise.
/// </summary>
public sealed record DeleteRoom(RoomKey Key) : WorldChange
{
    public override string EntityKind => "room";

    public override string EntityKey => Key.ToString();
}

/// <summary>Creates or repoints a single exit. One edge, one direction.</summary>
public sealed record SetExit(RoomKey From, Direction Direction, RoomKey To) : WorldChange
{
    public override string EntityKind => "exit";

    public override string EntityKey => $"{From}:{Direction.ToLowerName()}";
}

public sealed record RemoveExit(RoomKey From, Direction Direction) : WorldChange
{
    public override string EntityKind => "exit";

    public override string EntityKey => $"{From}:{Direction.ToLowerName()}";
}

// ---------------------------------------------------------------------------
// Requests - normalised by the loop into the primitives above
// ---------------------------------------------------------------------------

/// <summary>
/// Walk-and-build (PLAN.md §7.6). One request covering two situations, because the builder
/// should not have to know which one they are in:
/// <list type="bullet">
/// <item><description><b>Materialize</b> - an exit already points this way at a room that does
/// not exist, so the new room takes the key the exit already names and the link resolves.</description></item>
/// <item><description><b>Dig</b> - there is no exit that way, so both the room and the exit to
/// reach it are created.</description></item>
/// </list>
/// </summary>
public sealed record DigRoom(
    RoomKey From,
    Direction Direction,
    bool Reciprocal = true,
    string? ZoneKey = null,
    RoomKey? NewRoomKey = null) : WorldChange
{
    public override string EntityKind => "room";

    public override string EntityKey => From.ToString();
}

/// <summary>Links an exit, optionally with its reciprocal - the default (PLAN.md §7.6).</summary>
public sealed record LinkExit(
    RoomKey From,
    Direction Direction,
    RoomKey To,
    bool Reciprocal = true) : WorldChange
{
    public override string EntityKind => "exit";

    public override string EntityKey => $"{From}:{Direction.ToLowerName()}";
}

public sealed record UnlinkExit(
    RoomKey From,
    Direction Direction,
    bool Reciprocal = true) : WorldChange
{
    public override string EntityKind => "exit";

    public override string EntityKey => $"{From}:{Direction.ToLowerName()}";
}

/// <summary>
/// Changes a room's key, rewriting every exit that pointed at the old one in the same mutation
/// (PLAN.md §7.6) - otherwise renaming a dug room would silently orphan its neighbours.
/// </summary>
public sealed record RenameRoom(RoomKey From, RoomKey To) : WorldChange
{
    public override string EntityKind => "room";

    public override string EntityKey => From.ToString();
}

/// <summary>Sets or clears one flag on one room. A null value clears it (PLAN.md §4.10).</summary>
public sealed record SetRoomFlag(RoomKey Key, string Flag, bool? Value) : WorldChange
{
    public override string EntityKind => "room";

    public override string EntityKey => Key.ToString();
}

/// <summary>
/// Sets or clears one flag on one zone, leaving its siblings alone. A null value clears it, which
/// is not the same as setting it false - the key is removed, so the world above decides (§4.10).
/// </summary>
/// <remarks>
/// The alternative is what the builder did before this existed: read the whole flag map, edit one
/// key, and PATCH it back. Two builders working in one zone then erase each other, and the loss is
/// silent - the second write simply carries an older map. Rooms already had this primitive; the
/// scopes above them are where the blast radius is largest, so they needed it more.
/// </remarks>
public sealed record SetZoneFlag(string Key, string Flag, bool? Value) : WorldChange
{
    public override string EntityKind => "zone";

    public override string EntityKey => Key;
}

/// <summary>
/// Sets or clears one flag on one world. See <see cref="SetZoneFlag"/> - same shape, one level up.
/// </summary>
public sealed record SetWorldFlag(string Key, string Flag, bool? Value) : WorldChange
{
    public override string EntityKind => "world";

    public override string EntityKey => Key;
}

// ---------------------------------------------------------------------------
// Templates and Spawners
// ---------------------------------------------------------------------------

public sealed record UpsertMobTemplate(
    string Key,
    string Name,
    string Description,
    string Icon,
    int Level,
    int WanderIntervalPulses,
    Dictionary<string, object> BaseStats,
    int BaseXp,
    int BaseGold,
    Dictionary<string, object> Behavior,
    List<Dictionary<string, object>> Loot,
    List<MobAttack> Attacks) : WorldChange
{
    public override string EntityKind => "mob-template";

    public override string EntityKey => Key;
}

public sealed record DeleteMobTemplate(string Key) : WorldChange
{
    public override string EntityKind => "mob-template";

    public override string EntityKey => Key;
}

public sealed record UpsertItemTemplate(
    string Key,
    string Name,
    string Description,
    string Icon,
    ItemSlot? Slot,
    int Weight,
    int BaseValue,
    Dictionary<string, object> BaseStats,
    int? AttackDelayPulses,
    string? AttackVerb,
    bool IsQuestItem) : WorldChange
{
    public override string EntityKind => "item-template";

    public override string EntityKey => Key;
}

public sealed record DeleteItemTemplate(string Key) : WorldChange
{
    public override string EntityKind => "item-template";

    public override string EntityKey => Key;
}

public sealed record UpsertSpawner(
    Guid Id,
    string ZoneKey,
    string TemplateKey,
    TemplateKind TemplateKind,
    List<string> RoomKeys,
    int TargetCount,
    int RespawnSeconds,
    bool? Wanders) : WorldChange
{
    public override string EntityKind => "spawner";

    public override string EntityKey => Id.ToString();
}

public sealed record DeleteSpawner(Guid Id) : WorldChange
{
    public override string EntityKind => "spawner";

    public override string EntityKey => Id.ToString();
}

public sealed record UpsertQuest(
    string Key,
    string? ZoneKey,
    string Name,
    string Summary,
    string Description,
    string GiverMobKey,
    string TurninMobKey,
    string? RequiredItemKey,
    int RequiredCount,
    int RewardXp,
    int RewardGold,
    string? RewardItemKey,
    int RewardItemCount,
    List<string> PrerequisiteQuestKeys,
    bool IsRepeatable,
    bool AutoStart,
    Dictionary<string, string> Dialogue,
    int SortOrder) : WorldChange
{
    public override string EntityKind => "quest";

    public override string EntityKey => Key;
}

public sealed record DeleteQuest(string Key) : WorldChange
{
    public override string EntityKind => "quest";

    public override string EntityKey => Key;
}
