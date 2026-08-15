using DikuWeb.Domain.Abilities;
using System.Text.Json;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Spawning;
using DikuWeb.Domain.Worlds;
using DikuWeb.Persistence;
using DikuWeb.Persistence.Converters;
using Microsoft.EntityFrameworkCore;

namespace DikuWeb.Server.Building;

/// <summary>
/// Reads the authored world out of Postgres as a <see cref="WorldBundle"/> (PLAN.md §6, Phase 6).
/// </summary>
/// <remarks>
/// <para>
/// Reads come from the database rather than the loop's world, for the same reason
/// <see cref="BuilderQueries"/> does: enumerating <c>WorldState</c> from a request thread is the
/// race the single-writer rule exists to prevent (§2.1).
/// </para>
/// <para>
/// The interesting part is what a <em>scoped</em> export contains. Rooms, spawners, and quests
/// belong to a zone, so scoping those is a filter. Templates do not - a mob template is global -
/// so a zone export that carried no templates would produce a bundle that imports cleanly and
/// spawns nothing. Instead the scope is closed over references: every template the zone's
/// spawners place, every mob and item its quests name, and every item those mobs drop. The
/// result is a bundle that stands up on its own in an empty database, which is the only kind
/// worth moving between environments.
/// </para>
/// <para>
/// The closure deliberately stops at items. Mobs reference items through their loot tables and
/// items reference nothing, so one pass reaches a fixed point - there is no cycle here to
/// iterate against, and pretending otherwise would be a loop that always runs exactly twice.
/// </para>
/// </remarks>
public sealed class WorldExporter(DikuWebDbContext db, TimeProvider clock)
{
    /// <summary>
    /// Everything, or one world, or one zone. Returns null when the named world or zone does not
    /// exist - an empty bundle would be indistinguishable from a correct export of empty content.
    /// </summary>
    public async Task<WorldBundle?> ExportAsync(
        string? worldKey,
        string? zoneKey,
        CancellationToken cancellationToken)
    {
        var scope = await ResolveScopeAsync(worldKey, zoneKey, cancellationToken);

        if (scope is null)
        {
            return null;
        }

        var (kind, key, zoneKeys) = scope.Value;

        var worlds = await WorldsAsync(kind, key, zoneKeys, cancellationToken);
        var zones = await ZonesAsync(kind, zoneKeys, cancellationToken);
        var rooms = await RoomsAsync(kind, zoneKeys, cancellationToken);
        var spawners = await SpawnersAsync(kind, zoneKeys, cancellationToken);
        var quests = await QuestsAsync(kind, zoneKeys, cancellationToken);

        var (items, mobs) = await TemplatesAsync(kind, spawners, quests, cancellationToken);

        // Every ability, whatever the scope. An ability belongs to a Path rather than to a zone,
        // so there is nothing to filter it by - and a zone bundle that carried none would move a
        // crypt into an environment where the abilities meant to fight through it are whatever
        // that server happened to have.
        var abilities = await AbilitiesAsync(cancellationToken);

        return new WorldBundle(
            WorldBundle.CurrentFormatVersion,
            clock.GetUtcNow(),
            new BundleScope(kind, key),
            worlds,
            zones,
            rooms,
            items,
            mobs,
            abilities,
            spawners,
            quests);
    }

    private async Task<IReadOnlyList<BundleAbility>> AbilitiesAsync(CancellationToken cancellationToken)
    {
        var abilities = await db.Abilities.AsNoTracking()
            .OrderBy(a => a.Path)
            .ThenBy(a => a.UnlockLevel)
            .ThenBy(a => a.Key)
            .ToListAsync(cancellationToken);

        return [.. abilities.Select(a => new BundleAbility(
            a.Key, a.Path, a.UnlockLevel, a.Name, a.Description, a.CostType, a.CostValue,
            a.CooldownPulses, a.CastTimePulses, a.TargetingType,
            [.. a.Effects.Select(e =>
                new AbilityEffectSpec(e.Key, new Dictionary<string, string>(e.Params, StringComparer.Ordinal)))]))];
    }

    // -----------------------------------------------------------------------
    // Scope
    // -----------------------------------------------------------------------

    /// <summary>
    /// Turns the two optional filters into a scope kind and the set of zones it covers. A zone
    /// key wins over a world key, since it is the narrower of the two and answering "both" with
    /// anything other than the narrower one would be a surprise.
    /// </summary>
    private async Task<(string Kind, string? Key, IReadOnlyList<string> ZoneKeys)?> ResolveScopeAsync(
        string? worldKey,
        string? zoneKey,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(zoneKey))
        {
            var exists = await db.Zones.AsNoTracking()
                .AnyAsync(z => z.Key == zoneKey, cancellationToken);

            return exists ? ("zone", zoneKey, new[] { zoneKey }) : null;
        }

        if (!string.IsNullOrWhiteSpace(worldKey))
        {
            var exists = await db.Worlds.AsNoTracking()
                .AnyAsync(w => w.Key == worldKey, cancellationToken);

            if (!exists)
            {
                return null;
            }

            var zones = await db.Zones.AsNoTracking()
                .Where(z => z.WorldKey == worldKey)
                .Select(z => z.Key)
                .ToListAsync(cancellationToken);

            return ("world", worldKey, zones);
        }

        return ("all", null, Array.Empty<string>());
    }

    private static bool IsEverything(string kind) => kind == "all";

    // -----------------------------------------------------------------------
    // Per entity kind
    // -----------------------------------------------------------------------

    private async Task<IReadOnlyList<BundleWorld>> WorldsAsync(
        string kind,
        string? key,
        IReadOnlyList<string> zoneKeys,
        CancellationToken cancellationToken)
    {
        var query = db.Worlds.AsNoTracking();

        if (kind == "world")
        {
            query = query.Where(w => w.Key == key);
        }
        else if (kind == "zone")
        {
            // The world above a zone travels with it. Importing a zone into an environment that
            // does not have its world would otherwise leave the zone parented to nothing, and
            // multipliers resolve through the world (§4.4) - so the numbers would be wrong
            // rather than merely missing.
            var owners = await db.Zones.AsNoTracking()
                .Where(z => zoneKeys.Contains(z.Key))
                .Select(z => z.WorldKey)
                .Distinct()
                .ToListAsync(cancellationToken);

            query = query.Where(w => owners.Contains(w.Key));
        }

        var worlds = await query.OrderBy(w => w.SortOrder).ThenBy(w => w.Key)
            .ToListAsync(cancellationToken);

        return [.. worlds.Select(w => new BundleWorld(
            w.Key, w.Name, w.Description, w.SortOrder, Flags(w.Flags), Multipliers(w.Multipliers)))];
    }

    private async Task<IReadOnlyList<BundleZone>> ZonesAsync(
        string kind,
        IReadOnlyList<string> zoneKeys,
        CancellationToken cancellationToken)
    {
        var query = db.Zones.AsNoTracking();

        if (!IsEverything(kind))
        {
            query = query.Where(z => zoneKeys.Contains(z.Key));
        }

        var zones = await query.OrderBy(z => z.Key).ToListAsync(cancellationToken);

        return [.. zones.Select(z => new BundleZone(
            z.Key, z.WorldKey, z.Name, z.Description, z.MinLevel, z.MaxLevel,
            Flags(z.Flags), Multipliers(z.Multipliers)))];
    }

    private async Task<IReadOnlyList<BundleRoom>> RoomsAsync(
        string kind,
        IReadOnlyList<string> zoneKeys,
        CancellationToken cancellationToken)
    {
        var query = db.Rooms.AsNoTracking().Include(r => r.Exits);

        var rooms = IsEverything(kind)
            ? await query.OrderBy(r => r.Key).ToListAsync(cancellationToken)
            : await query.Where(r => zoneKeys.Contains(r.ZoneKey))
                .OrderBy(r => r.Key)
                .ToListAsync(cancellationToken);

        return
        [
            .. rooms.Select(r => new BundleRoom(
                r.Key.ToString(),
                r.ZoneKey,
                r.Title,
                r.Description,
                Flags(r.Flags),
                r.Grid,
                r.Legend,
                r.EditorX,
                r.EditorY,
                [
                    // Ordered by the compass, not by insertion, so two exports of the same
                    // unchanged zone are byte-identical and a diff shows only real edits.
                    .. r.Exits
                        .OrderBy(e => DirectionExtensions.All.ToList().IndexOf(e.Direction))
                        .Select(e => new BundleExit(e.Direction.ToLowerName(), e.ToRoomKey.ToString())),
                ])),
        ];
    }

    private async Task<IReadOnlyList<BundleSpawner>> SpawnersAsync(
        string kind,
        IReadOnlyList<string> zoneKeys,
        CancellationToken cancellationToken)
    {
        var query = db.Spawners.AsNoTracking();

        if (!IsEverything(kind))
        {
            query = query.Where(s => zoneKeys.Contains(s.ZoneKey));
        }

        var spawners = await query.OrderBy(s => s.Id).ToListAsync(cancellationToken);

        return [.. spawners.Select(s => new BundleSpawner(
            s.Id, s.ZoneKey, s.TemplateKey, s.TemplateKind,
            [.. s.RoomKeys], s.TargetCount, s.RespawnSeconds, s.Wanders, s.FightsAtLevel))];
    }

    private async Task<IReadOnlyList<BundleQuest>> QuestsAsync(
        string kind,
        IReadOnlyList<string> zoneKeys,
        CancellationToken cancellationToken)
    {
        var query = db.Quests.AsNoTracking();

        if (!IsEverything(kind))
        {
            query = query.Where(q => zoneKeys.Contains(q.ZoneKey));
        }

        var quests = await query.OrderBy(q => q.SortOrder).ThenBy(q => q.Key)
            .ToListAsync(cancellationToken);

        return [.. quests.Select(q => new BundleQuest(
            q.Key, q.ZoneKey, q.Name, q.Summary, q.Description,
            q.GiverMobKey, q.TurninMobKey, q.RequiredItemKey, q.RequiredCount,
            q.RewardXp, q.RewardGold, q.RewardItemKey, q.RewardItemCount,
            [.. q.PrerequisiteQuestKeys], q.IsRepeatable, q.AutoStart,
            new Dictionary<string, string>(q.Dialogue, StringComparer.Ordinal), q.SortOrder))];
    }

    /// <summary>
    /// The templates a scoped bundle needs to stand up on its own. See the class remarks for why
    /// this is a closure rather than a filter.
    /// </summary>
    private async Task<(IReadOnlyList<BundleItemTemplate> Items, IReadOnlyList<BundleMobTemplate> Mobs)>
        TemplatesAsync(
            string kind,
            IReadOnlyList<BundleSpawner> spawners,
            IReadOnlyList<BundleQuest> quests,
            CancellationToken cancellationToken)
    {
        var mobKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var itemKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!IsEverything(kind))
        {
            foreach (var spawner in spawners)
            {
                (spawner.TemplateKind == TemplateKind.Mob ? mobKeys : itemKeys)
                    .Add(spawner.TemplateKey);
            }

            foreach (var quest in quests)
            {
                Add(mobKeys, quest.GiverMobKey);
                Add(mobKeys, quest.TurninMobKey);
                Add(itemKeys, quest.RequiredItemKey);
                Add(itemKeys, quest.RewardItemKey);
            }
        }

        var mobQuery = db.MobTemplates.AsNoTracking();
        if (!IsEverything(kind))
        {
            mobQuery = mobQuery.Where(m => mobKeys.Contains(m.Key));
        }

        var mobs = await mobQuery.OrderBy(m => m.Key).ToListAsync(cancellationToken);

        // Loot is read after the mobs are known, because a required quest item usually arrives
        // through a drop rather than through a spawner - a zone bundle without it imports a quest
        // nothing can finish, which is exactly the silent failure §10 names.
        if (!IsEverything(kind))
        {
            foreach (var mob in mobs)
            {
                foreach (var entry in mob.Loot)
                {
                    if (entry.TryGetValue("itemTemplateKey", out var value))
                    {
                        Add(itemKeys, value?.ToString());
                    }
                }
            }
        }

        var itemQuery = db.ItemTemplates.AsNoTracking();
        if (!IsEverything(kind))
        {
            itemQuery = itemQuery.Where(i => itemKeys.Contains(i.Key));
        }

        var items = await itemQuery.OrderBy(i => i.Key).ToListAsync(cancellationToken);

        return (
            [
                .. items.Select(i => new BundleItemTemplate(
                    i.Key, i.Name, i.Description, i.Icon, i.Slot, i.Weight, i.BaseValue,
                    new Dictionary<string, object>(i.BaseStats),
                    i.AttackDelayPulses, i.AttackVerb, i.IsQuestItem)),
            ],
            [
                .. mobs.Select(m => new BundleMobTemplate(
                    m.Key, m.Name, m.Description, m.Icon, m.Level, m.WanderIntervalPulses,
                    new Dictionary<string, object>(m.BaseStats),
                    m.BaseXp, m.BaseGold,
                    new Dictionary<string, object>(m.Behavior),
                    [.. m.Loot],
                    [.. m.Attacks])),
            ]);

        static void Add(HashSet<string> set, string? key)
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                set.Add(key);
            }
        }
    }

    // -----------------------------------------------------------------------
    // Value shaping
    // -----------------------------------------------------------------------

    /// <summary>
    /// A flag map as raw JSON, through the same serialiser the database column uses - so an
    /// unrecognised flag comes out byte-identical rather than being flattened to the booleans
    /// this build happens to know (§4.10).
    /// </summary>
    internal static JsonElement Flags(FlagSet? flags) =>
        JsonDocument.Parse(FlagSetJson.Serialize(flags)).RootElement.Clone();

    internal static IReadOnlyDictionary<string, decimal> Multipliers(Multipliers m) =>
        new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            ["strength"] = m.Strength,
            ["health"] = m.Health,
            ["damage"] = m.Damage,
            ["xp"] = m.Xp,
            ["gold"] = m.Gold,
            ["itemValue"] = m.ItemValue,
            ["itemPower"] = m.ItemPower,
            ["spawnDensity"] = m.SpawnDensity,
        };
}
