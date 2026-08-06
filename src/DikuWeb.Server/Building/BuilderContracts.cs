using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Items;
using DikuWeb.Domain.Spawning;
using DikuWeb.Domain.Worlds;

namespace DikuWeb.Server.Building;

// ---------------------------------------------------------------------------
// Responses
// ---------------------------------------------------------------------------

public sealed record WorldResponse(
    string Key,
    string Name,
    string Description,
    int SortOrder,
    IReadOnlyDictionary<string, bool> Flags,
    int ZoneCount)
{
    public static WorldResponse From(World world, int zoneCount) =>
        new(world.Key, world.Name, world.Description, world.SortOrder, Flat(world.Flags), zoneCount);

    internal static IReadOnlyDictionary<string, bool> Flat(FlagSet flags) =>
        RoomFlags.All
            .Where(f => flags.BooleanOrNull(f.Key) is not null)
            .ToDictionary(f => f.Key, f => flags.BooleanOrNull(f.Key)!.Value, StringComparer.Ordinal);
}

public sealed record ZoneResponse(
    string Key,
    string WorldKey,
    string Name,
    string Description,
    int MinLevel,
    int MaxLevel,
    IReadOnlyDictionary<string, bool> Flags,
    int RoomCount)
{
    public static ZoneResponse From(Zone zone, int roomCount) =>
        new(zone.Key, zone.WorldKey, zone.Name, zone.Description, zone.MinLevel, zone.MaxLevel,
            WorldResponse.Flat(zone.Flags), roomCount);
}

public sealed record ExitResponse(string Direction, string To, bool TargetExists);

/// <summary>
/// A room as the builder sees it. Carries <em>resolved</em> flags alongside the room's own, so
/// the editor can grey out an inherited value and name where it came from (PLAN.md §4.10) -
/// otherwise a room that is PvP because of its zone looks identical to one that is not.
/// </summary>
public sealed record RoomResponse(
    string Key,
    string ZoneKey,
    string Title,
    string Description,
    IReadOnlyDictionary<string, bool> Flags,
    IReadOnlyList<ResolvedFlag> Resolved,
    IReadOnlyList<string> Grid,
    IReadOnlyDictionary<string, string> Legend,
    int? EditorX,
    int? EditorY,
    IReadOnlyList<ExitResponse> Exits);

public sealed record ResolvedFlag(string Key, bool Value, string Source, string Summary);

public sealed record RoomFlagResponse(string Key, bool Default, string Summary, string Phase);

/// <summary>An advisory warning. Never blocks a save (PLAN.md §7.4).</summary>
public sealed record ValidationWarning(string Kind, string EntityKey, string Message);

public sealed record ZoneValidation(
    string ZoneKey,
    IReadOnlyList<ValidationWarning> Warnings);

public sealed record UnfinishedRoom(string Key, string Title, int? EditorX, int? EditorY);

public sealed record AuditEntry(
    Guid Id,
    Guid? AccountId,
    string? Username,
    string EntityKind,
    string EntityKey,
    string Action,
    DateTimeOffset At);

// ---------------------------------------------------------------------------
// Requests
// ---------------------------------------------------------------------------

public sealed record SaveWorldRequest(
    string? Name,
    string? Description,
    int? SortOrder,
    IReadOnlyDictionary<string, bool>? Flags);

public sealed record SaveZoneRequest(
    string? WorldKey,
    string? Name,
    string? Description,
    int? MinLevel,
    int? MaxLevel,
    IReadOnlyDictionary<string, bool>? Flags);

public sealed record SaveRoomRequest(
    string? ZoneKey,
    string? Title,
    string? Description,
    IReadOnlyDictionary<string, bool>? Flags,
    IReadOnlyList<string>? Grid,
    IReadOnlyDictionary<string, string>? Legend,
    int? EditorX,
    int? EditorY);

public sealed record SaveExitRequest(string? To, bool Reciprocal = true);

/// <summary>PLAN.md §7.6. Every field optional: <c>{ "direction": "north" }</c> is the common case.</summary>
public sealed record DigRequest(
    string? Direction,
    bool Reciprocal = true,
    string? ZoneKey = null,
    string? NewRoomKey = null);

public sealed record RenameRoomRequest(string? NewKey);

// ---------------------------------------------------------------------------
// Templates and Spawners
// ---------------------------------------------------------------------------

public sealed record MobTemplateResponse(
    string Key,
    string Name,
    string Description,
    string Icon,
    int Level,
    Dictionary<string, object> BaseStats,
    int BaseXp,
    int BaseGold,
    Dictionary<string, object> Behavior,
    List<Dictionary<string, object>> Loot);

public sealed record SaveMobTemplateRequest(
    string? Name,
    string? Description,
    string? Icon,
    int? Level,
    Dictionary<string, object>? BaseStats,
    int? BaseXp,
    int? BaseGold,
    Dictionary<string, object>? Behavior,
    List<Dictionary<string, object>>? Loot);

public sealed record ItemTemplateResponse(
    string Key,
    string Name,
    string Description,
    string Icon,
    ItemSlot? Slot,
    int Weight,
    int BaseValue,
    Dictionary<string, object> BaseStats);

public sealed record SaveItemTemplateRequest(
    string? Name,
    string? Description,
    string? Icon,
    ItemSlot? Slot,
    int? Weight,
    int? BaseValue,
    Dictionary<string, object>? BaseStats);

public sealed record SpawnerResponse(
    Guid Id,
    string ZoneKey,
    string TemplateKey,
    TemplateKind TemplateKind,
    List<string> RoomKeys,
    int TargetCount,
    int RespawnSeconds,
    bool Sentinel);

public sealed record SaveSpawnerRequest(
    string? ZoneKey,
    string? TemplateKey,
    TemplateKind? TemplateKind,
    List<string>? RoomKeys,
    int? TargetCount,
    int? RespawnSeconds,
    bool? Sentinel);

/// <summary>
/// Multiplier preview for a zone: shows how templates resolve with current multipliers.
/// Used for difficulty tuning in the builder UI.
/// </summary>
public sealed record MultiplierPreviewRow(
    string TemplateKey,
    string TemplateName,
    TemplateKind Kind,
    Dictionary<string, object> BaseStats,
    Dictionary<string, int> ResolvedStats);

public sealed record MultiplierPreview(
    string ZoneKey,
    Dictionary<string, decimal> WorldMultipliers,
    Dictionary<string, decimal> ZoneMultipliers,
    List<MultiplierPreviewRow> Templates);
