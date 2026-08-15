using System.Text.Json;
using System.Text.Json.Serialization;
using DikuWeb.Domain.Abilities;
using DikuWeb.Domain.Characters;
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
/// import that resurrected deleted characters would be a bug rather than a feature.
/// </para>
/// <para>
/// <b>Abilities travel now, and used to be excluded.</b> The reason they were left out has
/// expired: <c>ReconcileAbilitiesAsync</c> rebuilt them from <c>AbilityCatalogue</c> on every
/// startup, so an imported row would have been corrected on the next boot. The reconcile now only
/// plants what is missing, which makes the table authoritative - and makes this bundle the way a
/// retune reaches another environment at all.
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
    IReadOnlyList<BundleAbility> Abilities,
    IReadOnlyList<BundleSpawner> Spawners,
    IReadOnlyList<BundleQuest> Quests,
    IReadOnlyList<BundleGameConfiguration> Configurations)
{
    /// <summary>
    /// The only format this build writes, and the only one it reads.
    /// </summary>
    /// <remarks>
    /// Refusing an unknown version is the one hard refusal in the whole import path. Everything
    /// else is advisory (§7.4), but a bundle whose shape this build does not understand cannot
    /// be partially applied usefully - it would import the fields that happened to match and
    /// silently drop the rest, which is the failure mode a version number exists to prevent.
    ///
    /// <b>7 because a bundle carries its own starter configurations.</b> A v6 bundle has no
    /// <c>configurations</c>, so read as v7 it would arrive with none — which is survivable, unlike
    /// the bumps below it, since a missing configuration is visible the moment somebody opens the
    /// panel. It is here because the exporter now *writes* a key a v6 reader would drop, and a file
    /// labelled 6 that carries a v7 field is a lie about its own shape. This is what lets a whole
    /// starter set move between servers — the Aldenmoor configuration and the Reaches one are two
    /// complete answers to "what does a new player meet", and swapping is the point (§4.16).
    ///
    /// <b>6 because an exit can refuse you and a quest can grant a capability.</b> A version 5
    /// bundle has no <c>requiredFlagKey</c>, <c>requiredItemKey</c> or <c>rewardFlagKey</c>, so
    /// read as v6 every one of them would deserialise to null - which is a gate that opens for
    /// anybody, and a chain that never attunes anyone to anything (§4.15). Both halves fail
    /// silently and in the dangerous direction, which is precisely what this number is here to
    /// refuse. The strong kind of bump, unlike the one below it.
    ///
    /// <b>5 because a spawner can pin the level its mobs fight at.</b> This is the weaker kind of
    /// bump, and worth saying so: a v4 bundle carries no pin, which reads correctly as "the zone
    /// decides" - exactly what a v4 spawner meant. It is here because the exporter now *writes* a
    /// key a v4 reader would drop, and a file labelled 4 that carries a v5 field is a lie about its
    /// own shape. Omitting the key when null was the alternative; it buys nothing and costs a
    /// serialisation special case.
    ///
    ///
    /// <b>4 because an ability carries a list of effects rather than one.</b> A version 3 bundle
    /// has <c>effectKey</c> and <c>effectParams</c> where this one has <c>effects</c>, so read as
    /// v4 every ability in it would arrive with an empty list - which is an ability that costs its
    /// resource and does nothing, the exact silent failure the version number is here to refuse.
    ///
    /// <b>3 because abilities travel now.</b> A version 2 bundle carries no abilities at all, so
    /// reading one as version 3 would import an empty ability list - and, if a "replace" mode ever
    /// existed, would read as "this environment should have none". Refusing is the honest answer:
    /// the older file genuinely cannot say what this one is being asked to.
    ///
    /// <b>2 because a spawner's wander setting changed shape and meaning.</b> Version 1 carried
    /// <c>sentinel: bool</c>, where false was the value every spawner had by default and meant
    /// *"these mobs wander"*. Version 2 carries <c>wanders: bool?</c>, where absent means *"follow
    /// the template"*. A v1 bundle read as v2 would deserialise the missing key to null and every
    /// spawner in it would quietly change behaviour - which is the silent partial apply this
    /// number exists to refuse, arriving through a rename rather than through a new field.
    /// </remarks>
    public const int CurrentFormatVersion = 7;
}

/// <summary>
/// A named starter configuration: where a new character wakes up, and what they are told
/// (PLAN.md §4.16).
/// </summary>
/// <remarks>
/// <b>There is deliberately no <c>isActive</c>.</b> The definitions are content and belong beside
/// the world they describe; which one a given server obeys is that environment's own state, like
/// which room a character is standing in. An import is a merge that runs against a server with
/// people on it, so a field arriving in a content file must never repoint where every new character
/// wakes up — activation stays an explicit call, audited as its own act.
///
/// Carried whole rather than scoped, like abilities: a configuration belongs to a server, not to a
/// zone, so a zone-scoped export carries all of them or none.
/// </remarks>
public sealed record BundleGameConfiguration(
    string Key,
    string Name,
    string Description,
    string StartingRoomKey,
    string WelcomeMessage);

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

public sealed record BundleExit(
    string Direction,
    string To,
    string? RequiredFlagKey = null,
    string? RequiredItemKey = null,
    string? RefusalMessage = null);

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

/// <summary>
/// One ability, whole. Unlike a zone-scoped entity there is nothing to scope an ability *to* -
/// abilities belong to a Path, not to a zone - so a scoped export carries all of them or none.
/// </summary>
/// <remarks>
/// Carried in full rather than as a diff against the catalogue, for the reason the catalogue
/// stopped being authoritative: the target environment's catalogue may not be this build's, and a
/// bundle that only said "cooldown 48" would mean different things depending on what it landed
/// beside.
/// </remarks>
public sealed record BundleAbility(
    string Key,
    [property: JsonConverter(typeof(NullableEnumConverter<CharacterPath>))]
    CharacterPath? Path,
    int UnlockLevel,
    string Name,
    string Description,
    [property: JsonConverter(typeof(NullableEnumConverter<CostType>))]
    CostType? CostType,
    int CostValue,
    long CooldownPulses,
    long? CastTimePulses,
    [property: JsonConverter(typeof(NullableEnumConverter<TargetingType>))]
    TargetingType? TargetingType,
    List<AbilityEffectSpec>? Effects);

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
    bool? Wanders,
    /// <summary>
    /// The level these mobs fight at, or null to let the zone decide (PLAN.md §4.7).
    /// </summary>
    /// <remarks>
    /// A nullable int rather than the word <c>SpawnerResponse.Level</c> carries. A bundle is a copy
    /// of the database and has no PATCH semantics to disambiguate against, so null here means only
    /// one thing - the same reason <see cref="Wanders"/> is a <c>bool?</c> here and a word there.
    /// </remarks>
    int? FightsAtLevel);

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
    string? RewardFlagKey,
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
