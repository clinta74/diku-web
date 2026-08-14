using System.Text.Json.Serialization;
using DikuWeb.Domain.Abilities;
using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Items;
using DikuWeb.Domain.Spawning;
using DikuWeb.Domain.Worlds;
using DikuWeb.Server.Infrastructure;

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
    Multipliers Multipliers,
    int ZoneCount)
{
    public static WorldResponse From(World world, int zoneCount) =>
        new(world.Key, world.Name, world.Description, world.SortOrder, Flat(world.Flags),
            world.Multipliers.Clone(), zoneCount);

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
    Multipliers Multipliers,
    int RoomCount)
{
    public static ZoneResponse From(Zone zone, int roomCount) =>
        new(zone.Key, zone.WorldKey, zone.Name, zone.Description, zone.MinLevel, zone.MaxLevel,
            WorldResponse.Flat(zone.Flags), zone.Multipliers.Clone(), roomCount);
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

/// <remarks>
/// <paramref name="Multipliers"/> is all-or-nothing: null leaves the stored set alone, and a
/// value replaces the whole set. It is not merged field by field, because
/// <see cref="Domain.Worlds.Multipliers"/> defaults every unspecified factor to 1.0 — a partial
/// object would silently reset the factors it omitted. The editor sends all eight.
/// </remarks>
public sealed record SaveWorldRequest(
    string? Name,
    string? Description,
    int? SortOrder,
    IReadOnlyDictionary<string, bool>? Flags,
    Multipliers? Multipliers);

/// <inheritdoc cref="SaveWorldRequest"/>
public sealed record SaveZoneRequest(
    string? WorldKey,
    string? Name,
    string? Description,
    int? MinLevel,
    int? MaxLevel,
    IReadOnlyDictionary<string, bool>? Flags,
    Multipliers? Multipliers);

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

/// <summary>
/// One flag, three states, at whichever of the three scopes the route names. <c>true</c> and
/// <c>false</c> are decisions made here; <c>null</c> removes the key so the level above decides.
/// </summary>
/// <remarks>
/// This exists so the editor never has to send a whole flag map to change one flag.
/// <see cref="SaveRoomRequest.Flags"/> and its world and zone equivalents replace the entire set,
/// which quietly discards whatever another builder set in the meantime.
/// </remarks>
public sealed record SetFlagRequest(bool? Value);

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
    int WanderIntervalPulses,
    Dictionary<string, object> BaseStats,
    int BaseXp,
    int BaseGold,
    Dictionary<string, object> Behavior,
    List<Dictionary<string, object>> Loot,
    List<MobAttack> Attacks);

public sealed record SaveMobTemplateRequest(
    string? Name,
    string? Description,
    string? Icon,
    int? Level,
    int? WanderIntervalPulses,
    Dictionary<string, object>? BaseStats,
    int? BaseXp,
    int? BaseGold,
    Dictionary<string, object>? Behavior,
    List<Dictionary<string, object>>? Loot,
    List<MobAttack>? Attacks);

/// <param name="Problems">
/// What the validator says about this ability as stored. Carried on the response rather than only
/// returned from a save, so the editor can show a warning about a row somebody else authored, or
/// one that arrived by import — the two cases where nobody saw a save-time refusal.
/// </param>
public sealed record AbilityResponse(
    string Key,
    CharacterPath Path,
    int UnlockLevel,
    string Name,
    string Description,
    CostType CostType,
    int CostValue,
    long CooldownPulses,
    long? CastTimePulses,
    TargetingType TargetingType,
    IReadOnlyList<AbilityEffectSpec> Effects,
    IReadOnlyList<AbilityProblemResponse> Problems);

public sealed record AbilityProblemResponse(string Severity, string Message);

/// <remarks>
/// The three enums carry <see cref="NullableEnumConverter{T}"/> for the same reason
/// <see cref="SaveItemTemplateRequest"/>'s slot does: a nullable enum sent as a string does not
/// bind without it, so <c>"path": "Warden"</c> would arrive as null and the save would be refused
/// for having no Path — a 400 that blames the caller for the server's deserialiser.
/// </remarks>
public sealed record SaveAbilityRequest(
    [property: JsonConverter(typeof(NullableEnumConverter<CharacterPath>))]
    CharacterPath? Path,
    int? UnlockLevel,
    string? Name,
    string? Description,
    [property: JsonConverter(typeof(NullableEnumConverter<CostType>))]
    CostType? CostType,
    int? CostValue,
    long? CooldownPulses,
    long? CastTimePulses,
    [property: JsonConverter(typeof(NullableEnumConverter<TargetingType>))]
    TargetingType? TargetingType,
    List<AbilityEffectSpec>? Effects);

public sealed record ItemTemplateResponse(
    string Key,
    string Name,
    string Description,
    string Icon,
    // Same converter as the request side. Without it this serialises as an integer while the
    // client reads a string, so a slot could not survive a load-edit-save round trip - and
    // Head, being 0, was falsy on the way through.
    [property: JsonConverter(typeof(NullableEnumConverter<ItemSlot>))]
    ItemSlot? Slot,
    int Weight,
    int BaseValue,
    Dictionary<string, object> BaseStats,
    int? AttackDelayPulses,
    string? AttackVerb,
    bool IsQuestItem);

public sealed record SaveItemTemplateRequest(
    string? Name,
    string? Description,
    string? Icon,
    [property: JsonConverter(typeof(NullableEnumConverter<ItemSlot>))]
    ItemSlot? Slot,
    int? Weight,
    int? BaseValue,
    Dictionary<string, object>? BaseStats,
    int? AttackDelayPulses,
    string? AttackVerb,
    bool? IsQuestItem);

/// <param name="Wander">One of <see cref="WanderMode"/>. Never null on the way out.</param>
public sealed record SpawnerResponse(
    Guid Id,
    string ZoneKey,
    string TemplateKey,
    TemplateKind TemplateKind,
    List<string> RoomKeys,
    int TargetCount,
    int RespawnSeconds,
    string Wander,
    /// <summary>
    /// The level mobs from this spawner will fight at (PLAN.md §4.7). Zero for an item spawner, or
    /// for a mob spawner whose template or zone has since been deleted.
    /// </summary>
    /// <remarks>
    /// Read-only: the server computes it, and a client sending one back is ignored. It is here so
    /// the room's spawner list can say what a placement will actually produce, which a template's
    /// authored level does not answer once a zone has scaled it.
    /// </remarks>
    int FightsAtLevel);

/// <param name="Wander">One of <see cref="WanderMode"/>, or null to leave it as it is.</param>
public sealed record SaveSpawnerRequest(
    string? ZoneKey,
    string? TemplateKey,
    TemplateKind? TemplateKind,
    List<string>? RoomKeys,
    int? TargetCount,
    int? RespawnSeconds,
    string? Wander);

/// <summary>
/// How a spawner answers "do these mobs wander": defer to the template, or override it.
/// </summary>
/// <remarks>
/// <b>A word rather than a nullable bool, because null already means something on this API.</b>
/// Every field of <see cref="SaveSpawnerRequest"/> is optional and a PATCH coalesces it against
/// what is stored - <c>request.X ?? existing.X</c> - so null spells *"leave this alone"* on every
/// other field. The stored value is genuinely three-valued
/// (<see cref="Domain.Spawning.Spawner.Wanders"/>), and a nullable bool would have made
/// "leave it alone" and "follow the template" the same request. Two meanings on one wire value is
/// how a builder clears a setting by not touching it.
/// </remarks>
public static class WanderMode
{
    /// <summary>Whatever the mob template says. The default, and the usual answer.</summary>
    public const string Template = "template";

    /// <summary>These mobs wander, whatever the template says.</summary>
    public const string Always = "always";

    /// <summary>These mobs stay put, whatever the template says.</summary>
    public const string Never = "never";

    /// <summary>The stored tri-state as the word that describes it.</summary>
    public static string From(bool? wanders) => wanders switch
    {
        true => Always,
        false => Never,
        null => Template,
    };

    /// <summary>
    /// The word as a stored tri-state. False when it is not one of the three.
    /// </summary>
    /// <remarks>
    /// Try-parse rather than a function returning the value, because what it parses to is itself
    /// nullable: "follow the template" *is* null. A method returning <c>bool?</c> would have to
    /// spell "that is not a mode" as null too, and a typo would silently mean the default.
    /// </remarks>
    public static bool TryParse(string? mode, out bool? wanders)
    {
        switch (mode)
        {
            case Template:
                wanders = null;
                return true;
            case Always:
                wanders = true;
                return true;
            case Never:
                wanders = false;
                return true;
            default:
                wanders = null;
                return false;
        }
    }
}

public sealed record QuestResponse(
    string Key,
    string ZoneKey,
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
    int SortOrder);

/// <summary>
/// One thing that would stop a quest being finishable. Advisory only, like every other builder
/// check - an unfinishable quest is still saveable, it just should not be a surprise.
/// </summary>
/// <param name="Kind">Machine-readable discriminator, e.g. "unreachable-required-item".</param>
/// <param name="Message">A sentence a builder can act on.</param>
public sealed record ReachabilityWarning(
    string Kind,
    string Message,
    string? ItemKey = null,
    string? MobKey = null);

public sealed record QuestReachability(
    string QuestKey,
    IReadOnlyList<ReachabilityWarning> Warnings);

public sealed record SaveQuestRequest(
    string? ZoneKey,
    string? Name,
    string? Summary,
    string? Description,
    string? GiverMobKey,
    string? TurninMobKey,
    string? RequiredItemKey,
    int? RequiredCount,
    int? RewardXp,
    int? RewardGold,
    string? RewardItemKey,
    int? RewardItemCount,
    List<string>? PrerequisiteQuestKeys,
    bool? IsRepeatable,
    bool? AutoStart,
    Dictionary<string, string>? Dialogue,
    int? SortOrder);

/// <summary>
/// Multiplier preview for a zone: shows how templates resolve with current multipliers.
/// Used for difficulty tuning in the builder UI.
/// </summary>
/// <param name="TemplateLevel">The level as authored. Zero for an item, which has no level.</param>
/// <param name="FightsAtLevel">
/// The level a mob spawned here actually fights at (PLAN.md §4.7). The difference between this and
/// <paramref name="TemplateLevel"/> is the whole point of the panel — a rat lifted to 20 by a zone's
/// dials pays and reads as a level 20 fight, and there was previously nowhere to see that before a
/// player felt it.
/// </param>
/// <param name="BaseStats">The template's own jsonb bag, verbatim.</param>
/// <param name="BaseValues">
/// The same numbers <paramref name="ResolvedStats"/> reports, before scaling, keyed identically.
/// <para>
/// It exists because the panel cannot do the join itself. A template may write its dice as
/// <c>"damage": "4-7"</c>, so the resolved <c>damageMin</c> has no counterpart in
/// <paramref name="BaseStats"/> and the Base column would read "—" for the one dial this whole
/// panel is about. Synthesising the missing keys into the template's own bag was the alternative,
/// and a bag that claims to be the template while carrying keys the template never had is a lie
/// the next reader trips over.
/// </para>
/// </param>
public sealed record MultiplierPreviewRow(
    string TemplateKey,
    string TemplateName,
    TemplateKind Kind,
    Dictionary<string, object> BaseStats,
    Dictionary<string, int> ResolvedStats,
    int TemplateLevel,
    int FightsAtLevel,
    Dictionary<string, int> BaseValues);

public sealed record MultiplierPreview(
    string ZoneKey,
    Dictionary<string, decimal> WorldMultipliers,
    Dictionary<string, decimal> ZoneMultipliers,
    List<MultiplierPreviewRow> Templates);
