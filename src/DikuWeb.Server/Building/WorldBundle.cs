using System.Text.Json;
using System.Text.Json.Serialization;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Items;
using DikuWeb.Domain.Spawning;
using DikuWeb.Server.Infrastructure;

namespace DikuWeb.Server.Building;

/// <summary>
/// The authored world as one JSON document (PLAN.md §6, Phase 6) - what moves content between
/// environments now that Postgres is the only source of truth and there are no world files.
/// </summary>
/// <remarks>
/// <para>
/// This carries <em>content</em> and nothing else: the same eight tables
/// <c>tools/export-content.sql</c> covers, for the same reasons. Accounts, characters, item
/// instances, character quests, and the two audit tables are player data and history, and an
/// import that resurrected deleted characters would be a bug rather than a feature. Abilities are
/// absent for a different reason - <c>ReconcileAbilitiesAsync</c> rebuilds them from
/// <c>AbilityCatalogue</c> on every startup, so importing them would write rows the next boot
/// only has to correct.
/// </para>
/// <para>
/// Entities are stored flat and keyed rather than nested under their parents. A room names its
/// zone rather than living inside it, which is what lets a bundle be scoped to a zone and still
/// carry the world above it, and what keeps the import order a property of the importer rather
/// than of the file. Exits are the one exception: they hang off their room, because an exit has
/// no identity of its own beyond the room and direction it leaves by.
/// </para>
/// </remarks>
public sealed record WorldBundle(
    int FormatVersion,
    DateTimeOffset ExportedAt,
    BundleScope Scope,
    IReadOnlyList<BundleWorld> Worlds,
    IReadOnlyList<BundleZone> Zones,
    IReadOnlyList<BundleRoom> Rooms,
    IReadOnlyList<BundleItemTemplate> ItemTemplates,
    IReadOnlyList<BundleMobTemplate> MobTemplates,
    IReadOnlyList<BundleSpawner> Spawners,
    IReadOnlyList<BundleQuest> Quests)
{
    /// <summary>
    /// The only format this build writes, and the only one it reads.
    /// </summary>
    /// <remarks>
    /// Refusing an unknown version is the one hard refusal in the whole import path. Everything
    /// else is advisory (§7.4), but a bundle whose shape this build does not understand cannot
    /// be partially applied usefully - it would import the fields that happened to match and
    /// silently drop the rest, which is the failure mode a version number exists to prevent.
    /// </remarks>
    public const int CurrentFormatVersion = 1;
}

/// <summary>
/// What the export was asked for, recorded so a bundle can say what it is rather than leaving
/// the reader to infer it from what happens to be inside.
/// </summary>
/// <param name="Kind">"all", "world", or "zone".</param>
/// <param name="Key">The world or zone key, or null when the scope is everything.</param>
public sealed record BundleScope(string Kind, string? Key);

public sealed record BundleWorld(
    string Key,
    string Name,
    string Description,
    int SortOrder,
    // Raw JSON rather than a bool map, so a flag this build does not recognise survives the
    // round trip the same way it survives a database one (§4.10). An export that dropped a
    // newer binary's flag would quietly rewrite content on its way between environments.
    JsonElement Flags,
    IReadOnlyDictionary<string, decimal> Multipliers);

public sealed record BundleZone(
    string Key,
    string WorldKey,
    string Name,
    string Description,
    int MinLevel,
    int MaxLevel,
    JsonElement Flags,
    IReadOnlyDictionary<string, decimal> Multipliers);

public sealed record BundleRoom(
    string Key,
    string ZoneKey,
    string Title,
    string Description,
    JsonElement Flags,
    IReadOnlyList<string> Grid,
    IReadOnlyDictionary<string, string> Legend,
    int? EditorX,
    int? EditorY,
    IReadOnlyList<BundleExit> Exits);

public sealed record BundleExit(string Direction, string To);

public sealed record BundleItemTemplate(
    string Key,
    string Name,
    string Description,
    string Icon,
    [property: JsonConverter(typeof(NullableEnumConverter<ItemSlot>))]
    ItemSlot? Slot,
    int Weight,
    int BaseValue,
    Dictionary<string, object> BaseStats,
    int? AttackDelayPulses,
    string? AttackVerb,
    bool IsQuestItem);

public sealed record BundleMobTemplate(
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

/// <remarks>
/// Carries its <see cref="Id"/>, which is what makes re-importing a bundle idempotent. A spawner
/// has no content key to collide on, so an import that minted a fresh id every time would double
/// the population of every zone it touched on the second run.
/// </remarks>
public sealed record BundleSpawner(
    Guid Id,
    string ZoneKey,
    string TemplateKey,
    TemplateKind TemplateKind,
    List<string> RoomKeys,
    int TargetCount,
    int RespawnSeconds,
    bool Sentinel);

public sealed record BundleQuest(
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

// ---------------------------------------------------------------------------
// Import reporting
// ---------------------------------------------------------------------------

/// <summary>
/// What an import did, or - under <c>dryRun</c> - what it would have done.
/// </summary>
/// <param name="Counts">Per entity kind, split by whether the key already existed here.</param>
/// <param name="Warnings">
/// Advisory, exactly as <c>/validate</c> is (§7.4): a reference the bundle does not carry and
/// this database does not have either. Never blocks the import, because a zone legitimately
/// imported ahead of the zone it links to is a state the world already tolerates.
/// </param>
/// <param name="Failures">
/// Entities the loop refused or that could not be persisted. Non-empty means the import was
/// partial - see <see cref="WorldImporter"/> for why that is possible.
/// </param>
public sealed record ImportReport(
    int FormatVersion,
    bool DryRun,
    IReadOnlyList<ImportCount> Counts,
    IReadOnlyList<ValidationWarning> Warnings,
    IReadOnlyList<ImportFailure> Failures)
{
    public bool Ok => Failures.Count == 0;
}

public sealed record ImportCount(string Kind, int Created, int Updated);

public sealed record ImportFailure(string Kind, string Key, string Message);
