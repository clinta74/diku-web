using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine;
using DikuWeb.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DikuWeb.Server.Building;

/// <summary>
/// Everything the builder <em>reads</em>, served from Postgres rather than from the loop's
/// in-memory world.
/// </summary>
/// <remarks>
/// This is not an oversight, it is the single-writer rule (PLAN.md §2.1) applied to reads.
/// <c>WorldState</c> is mutated by the loop thread with no locks; enumerating its dictionaries
/// from a request thread is a genuine race that would surface as an occasional
/// InvalidOperationException under exactly the conditions - a builder editing while the world
/// is busy - that the builder exists for.
///
/// Reading from the database instead is safe and correct, because every mutation is persisted
/// before its HTTP call returns. The database is at most one in-flight edit behind, and an
/// in-flight edit is one the builder has not been told succeeded yet.
/// </remarks>
public sealed class BuilderQueries(DikuWebDbContext db)
{
    public async Task<IReadOnlyList<WorldResponse>> WorldsAsync(CancellationToken cancellationToken)
    {
        var worlds = await db.Worlds.AsNoTracking()
            .OrderBy(w => w.SortOrder).ThenBy(w => w.Key)
            .ToListAsync(cancellationToken);

        var counts = await db.Zones.AsNoTracking()
            .GroupBy(z => z.WorldKey)
            .Select(g => new { WorldKey = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.WorldKey, x => x.Count, cancellationToken);

        return [.. worlds.Select(w => WorldResponse.From(w, counts.GetValueOrDefault(w.Key)))];
    }

    public async Task<WorldResponse?> WorldAsync(string key, CancellationToken cancellationToken)
    {
        var world = await db.Worlds.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Key == key, cancellationToken);

        if (world is null)
        {
            return null;
        }

        var zones = await db.Zones.CountAsync(z => z.WorldKey == key, cancellationToken);
        return WorldResponse.From(world, zones);
    }

    public async Task<IReadOnlyList<ZoneResponse>> ZonesAsync(
        string? worldKey,
        CancellationToken cancellationToken)
    {
        var query = db.Zones.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(worldKey))
        {
            query = query.Where(z => z.WorldKey == worldKey);
        }

        var zones = await query.OrderBy(z => z.Key).ToListAsync(cancellationToken);

        var counts = await db.Rooms.AsNoTracking()
            .GroupBy(r => r.ZoneKey)
            .Select(g => new { ZoneKey = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ZoneKey, x => x.Count, cancellationToken);

        return [.. zones.Select(z => ZoneResponse.From(z, counts.GetValueOrDefault(z.Key)))];
    }

    public async Task<ZoneResponse?> ZoneAsync(string key, CancellationToken cancellationToken)
    {
        var zone = await db.Zones.AsNoTracking().FirstOrDefaultAsync(z => z.Key == key, cancellationToken);

        if (zone is null)
        {
            return null;
        }

        var rooms = await db.Rooms.CountAsync(r => r.ZoneKey == key, cancellationToken);
        return ZoneResponse.From(zone, rooms);
    }

    public async Task<IReadOnlyList<RoomResponse>> RoomsAsync(
        string zoneKey,
        CancellationToken cancellationToken)
    {
        var rooms = await db.Rooms.AsNoTracking()
            .Include(r => r.Exits)
            .Where(r => r.ZoneKey == zoneKey)
            .OrderBy(r => r.Key)
            .ToListAsync(cancellationToken);

        if (rooms.Count == 0)
        {
            return [];
        }

        var (zoneFlags, worldFlags) = await InheritedAsync(zoneKey, cancellationToken);

        // One set lookup for every exit target in the zone, rather than a query per exit:
        // "does this exit dangle?" is asked for every room on every canvas open.
        var targets = rooms.SelectMany(r => r.Exits).Select(e => e.ToRoomKey).Distinct().ToList();
        var existing = await ExistingRoomsAsync(targets, cancellationToken);

        return [.. rooms.Select(r => Project(r, zoneFlags, worldFlags, existing))];
    }

    public async Task<RoomResponse?> RoomAsync(RoomKey key, CancellationToken cancellationToken)
    {
        var room = await db.Rooms.AsNoTracking()
            .Include(r => r.Exits)
            .FirstOrDefaultAsync(r => r.Key == key, cancellationToken);

        if (room is null)
        {
            return null;
        }

        var (zoneFlags, worldFlags) = await InheritedAsync(room.ZoneKey, cancellationToken);
        var existing = await ExistingRoomsAsync(
            [.. room.Exits.Select(e => e.ToRoomKey)], cancellationToken);

        return Project(room, zoneFlags, worldFlags, existing);
    }

    public async Task<IReadOnlyList<UnfinishedRoom>> UnfinishedAsync(
        string zoneKey,
        CancellationToken cancellationToken)
    {
        var rooms = await db.Rooms.AsNoTracking()
            .Where(r => r.ZoneKey == zoneKey)
            .OrderBy(r => r.Key)
            .ToListAsync(cancellationToken);

        // Filtered in memory rather than with a jsonb containment predicate: the set is one
        // zone's worth of rooms, and keeping the flag semantics in one place (BooleanOrNull,
        // which also handles a wrong-typed value) is worth more than pushing it into SQL.
        return
        [
            .. rooms
                .Where(r => r.Flags.BooleanOrNull(RoomFlags.Unfinished.Key) == true)
                .Select(r => new UnfinishedRoom(r.Key.ToString(), r.Title, r.EditorX, r.EditorY)),
        ];
    }

    public async Task<IReadOnlyList<AuditEntry>> AuditAsync(
        string? entityKind,
        string? entityKey,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = db.ContentAudits.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(entityKind))
        {
            query = query.Where(a => a.EntityKind == entityKind);
        }

        if (!string.IsNullOrWhiteSpace(entityKey))
        {
            query = query.Where(a => a.EntityKey == entityKey);
        }

        var rows = await query
            .OrderByDescending(a => a.At)
            .ThenByDescending(a => a.Id)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(cancellationToken);

        var accountIds = rows.Where(r => r.AccountId.HasValue).Select(r => r.AccountId!.Value).Distinct();
        var names = await db.Accounts.AsNoTracking()
            .Where(a => accountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Username, cancellationToken);

        return
        [
            .. rows.Select(a => new AuditEntry(
                a.Id,
                a.AccountId,
                a.AccountId.HasValue ? names.GetValueOrDefault(a.AccountId.Value) : null,
                a.EntityKind,
                a.EntityKey,
                a.Action.ToString(),
                a.At)),
        ];
    }

    // -----------------------------------------------------------------------
    // Templates and Spawners (Phase 3)
    // -----------------------------------------------------------------------

    public async Task<IReadOnlyList<MobTemplateResponse>> MobTemplatesAsync(
        CancellationToken cancellationToken)
    {
        var templates = await db.MobTemplates.AsNoTracking().OrderBy(t => t.Key).ToListAsync(cancellationToken);
        return [.. templates.Select(t => new MobTemplateResponse(
            t.Key, t.Name, t.Description, t.Icon, t.Level, t.WanderIntervalPulses,
            new Dictionary<string, object>(t.BaseStats),
            t.BaseXp, t.BaseGold,
            new Dictionary<string, object>(t.Behavior),
            new List<Dictionary<string, object>>(t.Loot),
            new List<MobAttack>(t.Attacks)))];
    }

    public async Task<MobTemplateResponse?> MobTemplateAsync(
        string key,
        CancellationToken cancellationToken)
    {
        var template = await db.MobTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Key == key, cancellationToken);
        if (template is null)
        {
            return null;
        }
        return new MobTemplateResponse(
            template.Key, template.Name, template.Description, template.Icon, template.Level, template.WanderIntervalPulses,
            new Dictionary<string, object>(template.BaseStats),
            template.BaseXp, template.BaseGold,
            new Dictionary<string, object>(template.Behavior),
            new List<Dictionary<string, object>>(template.Loot),
            new List<MobAttack>(template.Attacks));
    }

    public async Task<IReadOnlyList<ItemTemplateResponse>> ItemTemplatesAsync(
        CancellationToken cancellationToken)
    {
        var templates = await db.ItemTemplates.AsNoTracking().OrderBy(t => t.Key).ToListAsync(cancellationToken);
        return [.. templates.Select(t => new ItemTemplateResponse(
            t.Key, t.Name, t.Description, t.Icon, t.Slot, t.Weight, t.BaseValue,
            new Dictionary<string, object>(t.BaseStats),
            t.AttackDelayPulses, t.AttackVerb, t.IsQuestItem))];
    }

    public async Task<ItemTemplateResponse?> ItemTemplateAsync(
        string key,
        CancellationToken cancellationToken)
    {
        var template = await db.ItemTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Key == key, cancellationToken);
        if (template is null)
        {
            return null;
        }
        return new ItemTemplateResponse(
            template.Key, template.Name, template.Description, template.Icon, template.Slot,
            template.Weight, template.BaseValue,
            new Dictionary<string, object>(template.BaseStats),
            template.AttackDelayPulses, template.AttackVerb, template.IsQuestItem);
    }

    public async Task<IReadOnlyList<SpawnerResponse>> SpawnersAsync(
        string? zoneKey,
        CancellationToken cancellationToken)
    {
        var query = db.Spawners.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(zoneKey))
        {
            query = query.Where(s => s.ZoneKey == zoneKey);
        }

        var spawners = await query.OrderBy(s => s.Id).ToListAsync(cancellationToken);
        return [.. spawners.Select(s => new SpawnerResponse(
            s.Id, s.ZoneKey, s.TemplateKey, s.TemplateKind,
            new List<string>(s.RoomKeys), s.TargetCount, s.RespawnSeconds, s.Sentinel))];
    }

    public async Task<SpawnerResponse?> SpawnerAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var spawner = await db.Spawners.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (spawner is null)
        {
            return null;
        }
        return new SpawnerResponse(
            spawner.Id, spawner.ZoneKey, spawner.TemplateKey, spawner.TemplateKind,
            new List<string>(spawner.RoomKeys), spawner.TargetCount, spawner.RespawnSeconds, spawner.Sentinel);
    }

    public async Task<IReadOnlyList<QuestResponse>> QuestsAsync(CancellationToken cancellationToken)
    {
        var quests = await db.Quests.AsNoTracking()
            .OrderBy(q => q.SortOrder).ThenBy(q => q.Key)
            .ToListAsync(cancellationToken);

        return [.. quests.Select(q => new QuestResponse(
            q.Key, q.ZoneKey, q.Name, q.Summary, q.Description,
            q.GiverMobKey, q.TurninMobKey, q.RequiredItemKey, q.RequiredCount,
            q.RewardXp, q.RewardGold, q.RewardItemKey, q.RewardItemCount,
            q.PrerequisiteQuestKeys, q.IsRepeatable, q.AutoStart, q.Dialogue, q.SortOrder))];
    }

    public async Task<IReadOnlyList<QuestResponse>> QuestsByZoneAsync(
        string zoneKey,
        CancellationToken cancellationToken)
    {
        var quests = await db.Quests.AsNoTracking()
            .Where(q => q.ZoneKey == zoneKey)
            .OrderBy(q => q.SortOrder).ThenBy(q => q.Key)
            .ToListAsync(cancellationToken);

        return [.. quests.Select(q => new QuestResponse(
            q.Key, q.ZoneKey, q.Name, q.Summary, q.Description,
            q.GiverMobKey, q.TurninMobKey, q.RequiredItemKey, q.RequiredCount,
            q.RewardXp, q.RewardGold, q.RewardItemKey, q.RewardItemCount,
            q.PrerequisiteQuestKeys, q.IsRepeatable, q.AutoStart, q.Dialogue, q.SortOrder))];
    }

    public async Task<QuestResponse?> QuestAsync(string key, CancellationToken cancellationToken)
    {
        var quest = await db.Quests.AsNoTracking()
            .FirstOrDefaultAsync(q => q.Key == key, cancellationToken);

        if (quest is null)
        {
            return null;
        }

        return new QuestResponse(
            quest.Key, quest.ZoneKey, quest.Name, quest.Summary, quest.Description,
            quest.GiverMobKey, quest.TurninMobKey, quest.RequiredItemKey, quest.RequiredCount,
            quest.RewardXp, quest.RewardGold, quest.RewardItemKey, quest.RewardItemCount,
            quest.PrerequisiteQuestKeys, quest.IsRepeatable, quest.AutoStart, quest.Dialogue, quest.SortOrder);
    }

    /// <summary>
    /// Multiplier preview for a zone: shows how templates resolve with current multipliers.
    /// Used for difficulty tuning (PLAN.md §7.5).
    /// </summary>
    public async Task<MultiplierPreview?> PreviewAsync(string zoneKey, CancellationToken cancellationToken)
    {
        var zone = await db.Zones.AsNoTracking()
            .FirstOrDefaultAsync(z => z.Key == zoneKey, cancellationToken);
        if (zone is null)
        {
            return null;
        }

        var world = await db.Worlds.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Key == zone.WorldKey, cancellationToken);
        if (world is null)
        {
            return null;
        }

        // Get all spawners for this zone to find unique templates
        var spawners = await db.Spawners.AsNoTracking()
            .Where(s => s.ZoneKey == zoneKey)
            .ToListAsync(cancellationToken);

        var mobTemplateKeys = spawners
            .Where(s => s.TemplateKind == DikuWeb.Domain.Spawning.TemplateKind.Mob)
            .Select(s => s.TemplateKey)
            .Distinct()
            .ToList();

        var itemTemplateKeys = spawners
            .Where(s => s.TemplateKind == DikuWeb.Domain.Spawning.TemplateKind.Item)
            .Select(s => s.TemplateKey)
            .Distinct()
            .ToList();

        var mobTemplates = await db.MobTemplates.AsNoTracking()
            .Where(t => mobTemplateKeys.Contains(t.Key))
            .ToListAsync(cancellationToken);

        var itemTemplates = await db.ItemTemplates.AsNoTracking()
            .Where(t => itemTemplateKeys.Contains(t.Key))
            .ToListAsync(cancellationToken);

        var rows = new List<MultiplierPreviewRow>();

        // Add mob templates
        foreach (var mob in mobTemplates.OrderBy(t => t.Key))
        {
            var resolved = ResolveMobStats(mob, world.Multipliers, zone.Multipliers);
            rows.Add(new MultiplierPreviewRow(
                mob.Key,
                mob.Name,
                DikuWeb.Domain.Spawning.TemplateKind.Mob,
                new Dictionary<string, object>(mob.BaseStats),
                resolved));
        }

        // Add item templates
        foreach (var item in itemTemplates.OrderBy(t => t.Key))
        {
            var resolved = ResolveItemStats(item, world.Multipliers, zone.Multipliers);
            rows.Add(new MultiplierPreviewRow(
                item.Key,
                item.Name,
                DikuWeb.Domain.Spawning.TemplateKind.Item,
                new Dictionary<string, object>(item.BaseStats),
                resolved));
        }

        return new MultiplierPreview(
            zoneKey,
            MultipliersDictionary(world.Multipliers),
            MultipliersDictionary(zone.Multipliers),
            rows);
    }

    private static Dictionary<string, int> ResolveMobStats(
        DikuWeb.Domain.Inhabitants.MobTemplate template,
        DikuWeb.Domain.Worlds.Multipliers worldMults,
        DikuWeb.Domain.Worlds.Multipliers zoneMults)
    {
        var resolved = new Dictionary<string, int>(StringComparer.Ordinal);

        // Health: use Strength multiplier.
        //
        // Read through JsonBag, not by pattern-matching the runtime type. BaseStats is jsonb, so
        // every value arrives as a JsonElement - `health is int` was false for every template
        // that had ever been saved, and the preview reported 40 health for all of them.
        if (template.BaseStats.ContainsKey("health"))
        {
            resolved["health"] = DikuWeb.Domain.Worlds.Multipliers.Resolve(
                JsonBag.Int32(template.BaseStats, "health", 40),
                worldMults,
                zoneMults,
                DikuWeb.Domain.Worlds.MultiplierType.Strength);
        }

        // XP: use Xp multiplier
        resolved["xp"] = DikuWeb.Domain.Worlds.Multipliers.Resolve(
            template.BaseXp, worldMults, zoneMults, DikuWeb.Domain.Worlds.MultiplierType.Xp);

        // Gold: use Gold multiplier
        resolved["gold"] = DikuWeb.Domain.Worlds.Multipliers.Resolve(
            template.BaseGold, worldMults, zoneMults, DikuWeb.Domain.Worlds.MultiplierType.Gold);

        return resolved;
    }

    private static Dictionary<string, int> ResolveItemStats(
        DikuWeb.Domain.Items.ItemTemplate template,
        DikuWeb.Domain.Worlds.Multipliers worldMults,
        DikuWeb.Domain.Worlds.Multipliers zoneMults)
    {
        var resolved = new Dictionary<string, int>(StringComparer.Ordinal);

        // Value: use ItemValue multiplier
        resolved["value"] = DikuWeb.Domain.Worlds.Multipliers.Resolve(
            template.BaseValue, worldMults, zoneMults, DikuWeb.Domain.Worlds.MultiplierType.ItemValue);

        return resolved;
    }

    private static Dictionary<string, decimal> MultipliersDictionary(DikuWeb.Domain.Worlds.Multipliers mults)
    {
        return new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            ["strength"] = mults.Strength,
            ["health"] = mults.Health,
            ["damage"] = mults.Damage,
            ["xp"] = mults.Xp,
            ["gold"] = mults.Gold,
            ["itemValue"] = mults.ItemValue,
            ["itemPower"] = mults.ItemPower,
            ["spawnDensity"] = mults.SpawnDensity,
        };
    }

    // -----------------------------------------------------------------------
    // Validation (PLAN.md §7.4) - advisory only, never blocks a save
    // -----------------------------------------------------------------------

    public async Task<ZoneValidation> ValidateAsync(string zoneKey, CancellationToken cancellationToken)
    {
        var warnings = new List<ValidationWarning>();

        var rooms = await db.Rooms.AsNoTracking()
            .Include(r => r.Exits)
            .Where(r => r.ZoneKey == zoneKey)
            .ToListAsync(cancellationToken);

        if (rooms.Count == 0)
        {
            warnings.Add(new ValidationWarning("empty-zone", zoneKey, "This zone has no rooms."));
            return new ZoneValidation(zoneKey, warnings);
        }

        var (zoneFlags, worldFlags) = await InheritedAsync(zoneKey, cancellationToken);

        var targets = rooms.SelectMany(r => r.Exits).Select(e => e.ToRoomKey).Distinct().ToList();
        var existing = await ExistingRoomsAsync(targets, cancellationToken);

        foreach (var room in rooms.OrderBy(r => r.Key.ToString(), StringComparer.Ordinal))
        {
            foreach (var exit in room.Exits.Where(e => !existing.Contains(e.ToRoomKey)))
            {
                warnings.Add(new ValidationWarning(
                    "dangling-exit",
                    room.Key.ToString(),
                    $"{exit.Direction.ToLowerName()} points at '{exit.ToRoomKey}', which does not exist."));
            }

            foreach (var key in room.Flags.UnknownKeys)
            {
                warnings.Add(new ValidationWarning(
                    "unknown-flag",
                    room.Key.ToString(),
                    $"'{key}' is not a flag this server knows about. It is kept, but nothing reads it."));
            }

            // The one flag whose blast radius is larger than the thing you edited: setting pvp
            // on a zone makes every room in it lethal, including a town square somebody else
            // authored. Naming the rooms turns an invisible edit into a visible one (§7.4).
            var pvp = RoomFlags.Resolve(RoomFlags.Pvp, room.Flags, zoneFlags, worldFlags);
            if (pvp.Value && pvp.IsInherited)
            {
                warnings.Add(new ValidationWarning(
                    "inherited-pvp",
                    room.Key.ToString(),
                    $"PvP here, inherited from the {pvp.Source.ToString().ToLowerInvariant()}."));
            }

            if (string.IsNullOrWhiteSpace(room.Description))
            {
                warnings.Add(new ValidationWarning(
                    "no-description", room.Key.ToString(), "No description."));
            }

            if (room.Grid.Count > 0 && room.Grid.Select(row => row.Length).Distinct().Count() > 1)
            {
                warnings.Add(new ValidationWarning(
                    "ragged-grid",
                    room.Key.ToString(),
                    "Grid rows are different lengths; the map falls back to a plain rectangle."));
            }

            foreach (var glyph in room.Grid
                .SelectMany(row => row.Select(c => c.ToString()))
                .Distinct(StringComparer.Ordinal)
                .Where(g => !room.Legend.ContainsKey(g)))
            {
                warnings.Add(new ValidationWarning(
                    "legend-gap", room.Key.ToString(), $"Grid uses '{glyph}' with no legend entry."));
            }
        }

        // Orphans: no way in. Computed across the whole world, not just this zone, because a
        // zone entrance is normally reached from its neighbour.
        var inbound = await db.RoomExits.AsNoTracking()
            .Select(e => e.ToRoomKey)
            .Distinct()
            .ToListAsync(cancellationToken);

        var reachable = inbound.ToHashSet();

        foreach (var room in rooms.Where(r => !reachable.Contains(r.Key)))
        {
            warnings.Add(new ValidationWarning(
                "orphan-room", room.Key.ToString(), "Nothing links to this room."));
        }

        return new ZoneValidation(zoneKey, warnings);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task<(FlagSet? Zone, FlagSet? World)> InheritedAsync(
        string zoneKey,
        CancellationToken cancellationToken)
    {
        var zone = await db.Zones.AsNoTracking()
            .FirstOrDefaultAsync(z => z.Key == zoneKey, cancellationToken);

        if (zone is null)
        {
            return (null, null);
        }

        var world = await db.Worlds.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Key == zone.WorldKey, cancellationToken);

        return (zone.Flags, world?.Flags);
    }

    private async Task<HashSet<RoomKey>> ExistingRoomsAsync(
        IReadOnlyList<RoomKey> candidates,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        var found = await db.Rooms.AsNoTracking()
            .Where(r => candidates.Contains(r.Key))
            .Select(r => r.Key)
            .ToListAsync(cancellationToken);

        return [.. found];
    }

    private static RoomResponse Project(
        Room room,
        FlagSet? zoneFlags,
        FlagSet? worldFlags,
        HashSet<RoomKey> existingTargets) =>
        new(
            room.Key.ToString(),
            room.ZoneKey,
            room.Title,
            room.Description,
            WorldResponse.Flat(room.Flags),
            [
                .. RoomFlags.All.Select(flag =>
                {
                    var resolved = RoomFlags.Resolve(flag, room.Flags, zoneFlags, worldFlags);
                    return new ResolvedFlag(
                        flag.Key,
                        resolved.Value,
                        resolved.Source.ToString().ToLowerInvariant(),
                        flag.Summary);
                }),
            ],
            room.Grid,
            room.Legend,
            room.EditorX,
            room.EditorY,
            [
                .. room.Exits
                    .OrderBy(e => DirectionExtensions.All.ToList().IndexOf(e.Direction))
                    .Select(e => new ExitResponse(
                        e.Direction.ToLowerName(),
                        e.ToRoomKey.ToString(),
                        existingTargets.Contains(e.ToRoomKey))),
            ]);
}
