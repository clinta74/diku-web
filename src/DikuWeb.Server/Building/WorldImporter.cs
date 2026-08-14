using DikuWeb.Domain.Abilities;
using System.Text.Json;
using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Mutations;
using DikuWeb.Persistence;
using DikuWeb.Persistence.Converters;
using Microsoft.EntityFrameworkCore;

namespace DikuWeb.Server.Building;

/// <summary>
/// Applies a <see cref="WorldBundle"/> to this environment (PLAN.md §6, Phase 6).
/// </summary>
/// <remarks>
/// <para>
/// Every entity goes through <see cref="WorldEditor"/>, one primitive at a time, exactly as a
/// builder's save does. Nothing here writes to Postgres directly. That is what keeps the loop
/// the single writer of world state (§2.1), what makes imported content visible to players
/// already standing in the rooms, and what writes a <c>content_audit</c> row per entity - so an
/// import is as answerable after the fact as a hand edit, which for a change of this size is the
/// point.
/// </para>
/// <para>
/// <b>An import is a merge, not a mirror.</b> Keys in the bundle are upserted; keys this
/// environment has and the bundle does not are left alone. There is deliberately no "replace"
/// mode that deletes the difference: §10 already names a bad live edit with no rollback as a
/// standing risk, and a mode whose failure is *deleting a zone somebody else authored* is a
/// larger version of the same risk with no better recovery story. Removing content stays a
/// deliberate, per-entity act.
/// </para>
/// <para>
/// <b>An import is not atomic, and says so.</b> One mutation per entity is one loop round trip
/// and one transaction, so a failure part way through leaves the entities before it applied.
/// Making the whole bundle atomic would need a batch primitive the loop does not have, and the
/// honest mitigation is cheaper: <c>dryRun</c> reports every collision and dangling reference
/// without touching anything, and <see cref="ImportReport.Failures"/> names precisely what did
/// not land.
/// </para>
/// </remarks>
public sealed class WorldImporter(DikuWebDbContext db, WorldEditor editor)
{
    public async Task<ImportReport> ImportAsync(
        WorldBundle bundle,
        Guid? accountId,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        var existing = await ExistingKeysAsync(bundle, cancellationToken);
        var warnings = await WarningsAsync(bundle, cancellationToken);
        var counts = new List<ImportCount>();
        var failures = new List<ImportFailure>();

        // Order is dependency order, not the order of the file. Worlds before the zones that
        // name them, items before the mobs whose loot tables list them, rooms before the exits
        // that leave them. Nothing here is enforced by a foreign key (§6) - it is enforced by
        // wanting the intermediate states to read correctly to a player standing in the zone
        // while the import runs.
        await ApplyAsync("world", bundle.Worlds, w => w.Key, WorldChangeFor);
        await ApplyAsync("zone", bundle.Zones, z => z.Key, ZoneChangeFor);
        await ApplyAsync("item-template", bundle.ItemTemplates, i => i.Key, ItemChangeFor);
        await ApplyAsync("mob-template", bundle.MobTemplates, m => m.Key, MobChangeFor);
        await ApplyAsync("ability", bundle.Abilities, a => a.Key, AbilityChangeFor);
        await ApplyAsync("room", bundle.Rooms, r => r.Key, RoomChangeFor);
        await ApplyExitsAsync(bundle);
        await ApplyAsync("spawner", bundle.Spawners, s => s.Id.ToString(), SpawnerChangeFor);
        await ApplyAsync("quest", bundle.Quests, q => q.Key, QuestChangeFor);

        return new ImportReport(bundle.FormatVersion, dryRun, counts, warnings, failures);

        // Shared per-kind loop: count what would happen, then - unless this is a rehearsal -
        // make it happen. Counting before applying is what lets a dry run answer the same
        // question a real run does, from the same code.
        async Task ApplyAsync<T>(
            string kind,
            IReadOnlyList<T> entities,
            Func<T, string> keyOf,
            Func<T, WorldChange?> changeOf)
        {
            var created = 0;
            var updated = 0;

            foreach (var entity in entities ?? [])
            {
                var key = keyOf(entity);

                if (changeOf(entity) is not { } change)
                {
                    failures.Add(new ImportFailure(kind, key, $"'{key}' is not a valid {kind} key."));
                    continue;
                }

                if (existing.Contains((kind, key)))
                {
                    updated++;
                }
                else
                {
                    created++;
                }

                if (dryRun)
                {
                    continue;
                }

                var outcome = await editor.ApplyAsync(change, accountId, cancellationToken);

                if (!outcome.Ok)
                {
                    failures.Add(new ImportFailure(kind, key, Describe(outcome)));
                }
            }

            counts.Add(new ImportCount(kind, created, updated));
        }

        // Exits are counted per edge rather than per room, since that is the unit that lands or
        // fails, and one unparseable direction should not condemn the other three.
        async Task ApplyExitsAsync(WorldBundle b)
        {
            var created = 0;
            var updated = 0;

            foreach (var room in b.Rooms ?? [])
            {
                if (!RoomKey.TryParse(room.Key, out var from))
                {
                    // Already reported by the room pass; reporting it again per exit would bury
                    // the one line that says what actually went wrong.
                    continue;
                }

                foreach (var exit in room.Exits ?? [])
                {
                    var label = $"{room.Key}:{exit.Direction}";

                    if (!DirectionExtensions.TryParse(exit.Direction, out var direction))
                    {
                        failures.Add(new ImportFailure(
                            "exit", label, $"'{exit.Direction}' is not a direction."));
                        continue;
                    }

                    if (!RoomKey.TryParse(exit.To, out var to))
                    {
                        failures.Add(new ImportFailure(
                            "exit", label, $"'{exit.To}' is not a room key."));
                        continue;
                    }

                    if (existing.Contains(("exit", $"{from}:{direction.ToLowerName()}")))
                    {
                        updated++;
                    }
                    else
                    {
                        created++;
                    }

                    if (dryRun)
                    {
                        continue;
                    }

                    // SetExit, not LinkExit: an export already carries both halves of a two-way
                    // link as separate edges, so asking for the reciprocal would invent an exit
                    // the source environment does not have.
                    var outcome = await editor.ApplyAsync(
                        new SetExit(from, direction, to), accountId, cancellationToken);

                    if (!outcome.Ok)
                    {
                        failures.Add(new ImportFailure("exit", label, Describe(outcome)));
                    }
                }
            }

            counts.Add(new ImportCount("exit", created, updated));
        }
    }

    private static string Describe(EditOutcome outcome) => outcome.Status switch
    {
        EditStatus.NotSaved => "Applied to the world but could not be persisted; it was rolled back.",
        _ => outcome.Result.Message ?? "Refused.",
    };

    // -----------------------------------------------------------------------
    // Bundle entity to mutation primitive
    // -----------------------------------------------------------------------

    private static WorldChange WorldChangeFor(BundleWorld w) =>
        new UpsertWorld(w.Key, w.Name, w.Description, w.SortOrder, Flags(w.Flags), Multipliers(w.Multipliers));

    private static WorldChange ZoneChangeFor(BundleZone z) =>
        new UpsertZone(z.Key, z.WorldKey, z.Name, z.Description, z.MinLevel, z.MaxLevel,
            Flags(z.Flags), Multipliers(z.Multipliers));

    private static WorldChange? RoomChangeFor(BundleRoom r) =>
        RoomKey.TryParse(r.Key, out var key)
            ? new UpsertRoom(key, r.ZoneKey, r.Title, r.Description, Flags(r.Flags),
                r.Grid ?? [], r.Legend ?? new Dictionary<string, string>(StringComparer.Ordinal),
                r.EditorX, r.EditorY)
            : null;

    private static WorldChange ItemChangeFor(BundleItemTemplate i) =>
        new UpsertItemTemplate(i.Key, i.Name, i.Description, i.Icon, i.Slot, i.Weight, i.BaseValue,
            i.BaseStats ?? [], i.AttackDelayPulses, i.AttackVerb, i.IsQuestItem);

    private static WorldChange MobChangeFor(BundleMobTemplate m) =>
        new UpsertMobTemplate(m.Key, m.Name, m.Description, m.Icon, m.Level, m.WanderIntervalPulses,
            m.BaseStats ?? [], m.BaseXp, m.BaseGold, m.Behavior ?? [], m.Loot ?? [], m.Attacks ?? []);

    /// <remarks>
    /// A missing enum reads as its safe default rather than throwing: the bundle may have been
    /// written by a build that spelled one differently, and the importer's job is to land what it
    /// can and report the rest. A wrong Path is visible immediately - the validator refuses a key
    /// whose prefix does not match it - where a thrown import leaves the whole bundle unapplied.
    /// </remarks>
    private static WorldChange AbilityChangeFor(BundleAbility a) =>
        new UpsertAbility(
            a.Key,
            a.Path ?? CharacterPath.Warden,
            a.UnlockLevel,
            a.Name,
            a.Description,
            a.CostType ?? Domain.Abilities.CostType.Stamina,
            a.CostValue,
            a.CooldownPulses,
            a.CastTimePulses,
            a.TargetingType ?? Domain.Abilities.TargetingType.SingleTarget,
            [.. (a.Effects ?? []).Select(e =>
                new AbilityEffectSpec(e.Key, new Dictionary<string, string>(e.Params, StringComparer.Ordinal)))]);

    private static WorldChange SpawnerChangeFor(BundleSpawner s) =>
        new UpsertSpawner(s.Id, s.ZoneKey, s.TemplateKey, s.TemplateKind,
            s.RoomKeys ?? [], s.TargetCount, s.RespawnSeconds, s.Wanders);

    private static WorldChange QuestChangeFor(BundleQuest q) =>
        new UpsertQuest(q.Key, q.ZoneKey, q.Name, q.Summary, q.Description,
            q.GiverMobKey, q.TurninMobKey, q.RequiredItemKey, q.RequiredCount,
            q.RewardXp, q.RewardGold, q.RewardItemKey, q.RewardItemCount,
            q.PrerequisiteQuestKeys ?? [], q.IsRepeatable, q.AutoStart,
            q.Dialogue ?? new Dictionary<string, string>(StringComparer.Ordinal), q.SortOrder);

    /// <summary>
    /// Reads a flag map back through the database's own serialiser, so a flag this build does not
    /// recognise is carried in rather than dropped - the mirror of the export side (§4.10).
    /// </summary>
    /// <remarks>
    /// This is the one place the import is deliberately more permissive than the builder API,
    /// which drops unknown keys on the way in. There a builder is typing a new flag into
    /// existence, which is worth refusing; here the flag already exists in another environment,
    /// and dropping it would silently rewrite content in transit.
    /// </remarks>
    private static FlagSet Flags(JsonElement flags) =>
        flags.ValueKind == JsonValueKind.Object
            ? FlagSetJson.Deserialize(flags.GetRawText())
            : new FlagSet();

    private static Multipliers Multipliers(IReadOnlyDictionary<string, decimal>? values)
    {
        var m = new Multipliers();

        if (values is null)
        {
            return m;
        }

        // Each factor read by name and defaulted to the class's own 1.0. An absent key is "not
        // stated", which for a multiplier is the identity - the same reading the builder's
        // all-or-nothing PATCH takes.
        m.Strength = Read("strength", m.Strength);
        m.Health = Read("health", m.Health);
        m.Damage = Read("damage", m.Damage);
        m.Xp = Read("xp", m.Xp);
        m.Gold = Read("gold", m.Gold);
        m.ItemValue = Read("itemValue", m.ItemValue);
        m.ItemPower = Read("itemPower", m.ItemPower);
        m.SpawnDensity = Read("spawnDensity", m.SpawnDensity);

        return m;

        decimal Read(string key, decimal fallback) =>
            values.TryGetValue(key, out var value) ? value : fallback;
    }

    // -----------------------------------------------------------------------
    // What is already here
    // -----------------------------------------------------------------------

    /// <summary>
    /// The subset of the bundle's keys this environment already holds, so the report can say
    /// created or updated rather than just "written".
    /// </summary>
    /// <remarks>
    /// Queried per kind against the bundle's keys rather than by loading every key in the
    /// database: a full-world import into a large environment would otherwise pull the whole
    /// content key space into memory to answer a question about the keys it already has in hand.
    /// </remarks>
    private async Task<HashSet<(string Kind, string Key)>> ExistingKeysAsync(
        WorldBundle bundle,
        CancellationToken cancellationToken)
    {
        var found = new HashSet<(string, string)>();

        var worldKeys = Keys(bundle.Worlds, w => w.Key);
        foreach (var key in await db.Worlds.AsNoTracking()
            .Where(w => worldKeys.Contains(w.Key)).Select(w => w.Key).ToListAsync(cancellationToken))
        {
            found.Add(("world", key));
        }

        var zoneKeys = Keys(bundle.Zones, z => z.Key);
        foreach (var key in await db.Zones.AsNoTracking()
            .Where(z => zoneKeys.Contains(z.Key)).Select(z => z.Key).ToListAsync(cancellationToken))
        {
            found.Add(("zone", key));
        }

        var itemKeys = Keys(bundle.ItemTemplates, i => i.Key);
        foreach (var key in await db.ItemTemplates.AsNoTracking()
            .Where(i => itemKeys.Contains(i.Key)).Select(i => i.Key).ToListAsync(cancellationToken))
        {
            found.Add(("item-template", key));
        }

        var mobKeys = Keys(bundle.MobTemplates, m => m.Key);
        foreach (var key in await db.MobTemplates.AsNoTracking()
            .Where(m => mobKeys.Contains(m.Key)).Select(m => m.Key).ToListAsync(cancellationToken))
        {
            found.Add(("mob-template", key));
        }

        var abilityKeys = Keys(bundle.Abilities, a => a.Key);
        foreach (var key in await db.Abilities.AsNoTracking()
            .Where(a => abilityKeys.Contains(a.Key)).Select(a => a.Key).ToListAsync(cancellationToken))
        {
            found.Add(("ability", key));
        }

        var questKeys = Keys(bundle.Quests, q => q.Key);
        foreach (var key in await db.Quests.AsNoTracking()
            .Where(q => questKeys.Contains(q.Key)).Select(q => q.Key).ToListAsync(cancellationToken))
        {
            found.Add(("quest", key));
        }

        var spawnerIds = (bundle.Spawners ?? []).Select(s => s.Id).ToList();
        foreach (var id in await db.Spawners.AsNoTracking()
            .Where(s => spawnerIds.Contains(s.Id)).Select(s => s.Id).ToListAsync(cancellationToken))
        {
            found.Add(("spawner", id.ToString()));
        }

        var roomKeys = (bundle.Rooms ?? [])
            .Select(r => RoomKey.TryParse(r.Key, out var k) ? k : (RoomKey?)null)
            .OfType<RoomKey>()
            .ToList();

        foreach (var key in await db.Rooms.AsNoTracking()
            .Where(r => roomKeys.Contains(r.Key)).Select(r => r.Key).ToListAsync(cancellationToken))
        {
            found.Add(("room", key.ToString()));
        }

        foreach (var exit in await db.RoomExits.AsNoTracking()
            .Where(e => roomKeys.Contains(e.FromRoomKey))
            .Select(e => new { e.FromRoomKey, e.Direction })
            .ToListAsync(cancellationToken))
        {
            found.Add(("exit", $"{exit.FromRoomKey}:{exit.Direction.ToLowerName()}"));
        }

        return found;

        static List<string> Keys<T>(IReadOnlyList<T>? entities, Func<T, string> keyOf) =>
            [.. (entities ?? []).Select(keyOf)];
    }

    // -----------------------------------------------------------------------
    // Advisory warnings (PLAN.md §7.4) - never block the import
    // -----------------------------------------------------------------------

    /// <summary>
    /// References the bundle does not carry and this environment does not have either. Everything
    /// a scoped export closes over should already be inside, so a warning here usually means the
    /// bundle was hand-edited or the two environments have genuinely diverged.
    /// </summary>
    private async Task<IReadOnlyList<ValidationWarning>> WarningsAsync(
        WorldBundle bundle,
        CancellationToken cancellationToken)
    {
        var warnings = new List<ValidationWarning>();

        var worlds = await ResolvableAsync(
            bundle.Worlds, w => w.Key, db.Worlds.AsNoTracking().Select(w => w.Key), cancellationToken);
        var zones = await ResolvableAsync(
            bundle.Zones, z => z.Key, db.Zones.AsNoTracking().Select(z => z.Key), cancellationToken);
        var items = await ResolvableAsync(
            bundle.ItemTemplates, i => i.Key, db.ItemTemplates.AsNoTracking().Select(i => i.Key), cancellationToken);
        var mobs = await ResolvableAsync(
            bundle.MobTemplates, m => m.Key, db.MobTemplates.AsNoTracking().Select(m => m.Key), cancellationToken);
        var quests = await ResolvableAsync(
            bundle.Quests, q => q.Key, db.Quests.AsNoTracking().Select(q => q.Key), cancellationToken);

        var rooms = new HashSet<string>(
            (bundle.Rooms ?? []).Select(r => r.Key), StringComparer.OrdinalIgnoreCase);
        foreach (var key in await db.Rooms.AsNoTracking().Select(r => r.Key).ToListAsync(cancellationToken))
        {
            rooms.Add(key.ToString());
        }

        foreach (var zone in bundle.Zones ?? [])
        {
            Check(worlds, zone.WorldKey, "missing-world", "zone", zone.Key,
                $"names world '{zone.WorldKey}', which is neither in this bundle nor here.");
        }

        foreach (var room in bundle.Rooms ?? [])
        {
            Check(zones, room.ZoneKey, "missing-zone", "room", room.Key,
                $"names zone '{room.ZoneKey}', which is neither in this bundle nor here.");

            // Dangling exits are a state the world already tolerates (§7.4), so this is a note,
            // not a refusal - it is normal and expected when importing one zone of several.
            foreach (var exit in room.Exits ?? [])
            {
                Check(rooms, exit.To, "dangling-exit", "room", room.Key,
                    $"{exit.Direction} points at '{exit.To}', which is neither in this bundle nor here.");
            }
        }

        foreach (var spawner in bundle.Spawners ?? [])
        {
            Check(zones, spawner.ZoneKey, "missing-zone", "spawner", spawner.Id.ToString(),
                $"names zone '{spawner.ZoneKey}', which is neither in this bundle nor here.");

            var kind = spawner.TemplateKind == Domain.Spawning.TemplateKind.Mob ? mobs : items;
            Check(kind, spawner.TemplateKey, "missing-template", "spawner", spawner.Id.ToString(),
                $"places '{spawner.TemplateKey}', which is neither in this bundle nor here.");
        }

        foreach (var quest in bundle.Quests ?? [])
        {
            Check(zones, quest.ZoneKey, "missing-zone", "quest", quest.Key,
                $"names zone '{quest.ZoneKey}', which is neither in this bundle nor here.");
            Check(mobs, quest.GiverMobKey, "missing-mob", "quest", quest.Key,
                $"is offered by '{quest.GiverMobKey}', which is neither in this bundle nor here.");
            Check(mobs, quest.TurninMobKey, "missing-mob", "quest", quest.Key,
                $"is handed in to '{quest.TurninMobKey}', which is neither in this bundle nor here.");
            Check(items, quest.RequiredItemKey, "missing-item", "quest", quest.Key,
                $"requires '{quest.RequiredItemKey}', which is neither in this bundle nor here.");
            Check(items, quest.RewardItemKey, "missing-item", "quest", quest.Key,
                $"rewards '{quest.RewardItemKey}', which is neither in this bundle nor here.");

            foreach (var prerequisite in quest.PrerequisiteQuestKeys ?? [])
            {
                Check(quests, prerequisite, "missing-quest", "quest", quest.Key,
                    $"waits on '{prerequisite}', which is neither in this bundle nor here.");
            }
        }

        return warnings;

        void Check(HashSet<string> known, string? key, string kind, string subject, string subjectKey, string message)
        {
            if (!string.IsNullOrWhiteSpace(key) && !known.Contains(key))
            {
                warnings.Add(new ValidationWarning(kind, subjectKey, $"This {subject} {message}"));
            }
        }
    }

    /// <summary>Every key of a kind that will exist once the bundle lands: its own plus this environment's.</summary>
    private static async Task<HashSet<string>> ResolvableAsync<T>(
        IReadOnlyList<T>? entities,
        Func<T, string> keyOf,
        IQueryable<string> here,
        CancellationToken cancellationToken)
    {
        var set = new HashSet<string>(
            (entities ?? []).Select(keyOf), StringComparer.OrdinalIgnoreCase);

        foreach (var key in await here.ToListAsync(cancellationToken))
        {
            set.Add(key);
        }

        return set;
    }
}
