using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Quests;
using DikuWeb.Domain.Spawning;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Inhabitants;
using DikuWeb.Engine.Presentation;

namespace DikuWeb.Server.Building;

/// <summary>How much a finding matters. Only <see cref="Error"/> stops anything.</summary>
public enum BundleFindingLevel
{
    /// <summary>Worth reading. Several are things content is allowed to be.</summary>
    Warning,

    /// <summary>No reading of the content under which this works.</summary>
    Error,
}

public sealed record BundleFinding(BundleFindingLevel Level, string Message);

/// <summary>What a pre-flight check found.</summary>
public sealed record BundleCheck(IReadOnlyList<BundleFinding> Findings)
{
    public IEnumerable<BundleFinding> Errors =>
        Findings.Where(f => f.Level == BundleFindingLevel.Error);

    public IEnumerable<BundleFinding> Warnings =>
        Findings.Where(f => f.Level == BundleFindingLevel.Warning);

    public bool Ok => !Errors.Any();
}

/// <summary>
/// Checks a <see cref="WorldBundle"/> before anyone tries to import it (PLAN.md §6).
/// </summary>
/// <remarks>
/// <para>
/// <b>A pre-flight check, not a replacement for <c>?dryRun=true</c>.</b> The dry run is
/// authoritative: it knows what is already in the target database and it is the same code path a
/// real import takes. This needs neither a server nor a database, which is what makes it useful in
/// an editor loop and in the test suite.
/// </para>
/// <para>
/// Three of these are checks the dry run deliberately does not make, because they are authoring
/// mistakes rather than import failures:
/// </para>
/// <list type="bullet">
///   <item><b>Reciprocity.</b> An import applies each edge as given and never invents the return,
///   so a bundle that only says <c>north</c> produces a one-way corridor which imports perfectly
///   and reads as a bug the first time somebody walks it. A <em>warning</em>, and it has to stay
///   one: a one-way can be the story — a mirror you arrive through and cannot go back out of — and
///   nothing here can tell that from a slip.</item>
///   <item><b>Connectivity.</b> A room with no path to the rest of the bundle imports fine and is
///   reachable only by <c>goto</c>.</item>
///   <item><b>A room inside its own zone.</b> <c>ossara.gatetown.x</c> declaring
///   <c>zoneKey: ossara.brackenfell</c> is legal to the engine and is almost always a paste.</item>
/// </list>
/// <para>
/// <b>This was a Python script, and porting it turned four regexes into references.</b> It used to
/// recover the flag registry, the non-placeable tiles, the dialogue keys and the mob behavior keys
/// by running regular expressions over the C# — a second transcription of each, and so a second
/// thing to get wrong. It now reads <see cref="RoomFlags"/>, <see cref="RoomLayoutService"/>,
/// <see cref="QuestDialogue"/> and <see cref="MobBehavior"/> outright, and gets
/// <see cref="RoomKey.TryParse"/> and <see cref="DirectionExtensions.TryParse"/> for free rather
/// than reimplementing the key grammar and the compass.
/// </para>
/// <para>
/// The behavior-key pass is <b>stricter</b> than the script it replaces, and that is the port
/// earning itself: the old regex swept every lowercase literal out of <c>MobBehavior.cs</c>, so it
/// accepted <c>aggressive</c>, <c>npc</c> and <c>passive</c> — <em>values</em> of the type key — as
/// though they were bag keys of their own.
/// </para>
/// </remarks>
public static class BundleValidator
{
    /// <summary>
    /// The fewest open cells a room may leave.
    /// </summary>
    /// <remarks>
    /// Entities are placed only on open ground and are simply not drawn when there is none, so a
    /// room that is all water is a room whose occupants vanish. This is the validator's own rule
    /// rather than a copy of an engine constant — nothing at runtime enforces a floor.
    /// </remarks>
    public const int MinOpenCells = 40;

    public static BundleCheck Validate(WorldBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        var findings = new List<BundleFinding>();

        void Error(string message) => findings.Add(new BundleFinding(BundleFindingLevel.Error, message));
        void Warn(string message) => findings.Add(new BundleFinding(BundleFindingLevel.Warning, message));

        if (!BundleFormat.IsCurrent(bundle))
        {
            Error(BundleFormat.VersionRefusal(bundle.FormatVersion));
        }

        var worlds = bundle.Worlds.Select(w => w.Key).ToHashSet(StringComparer.Ordinal);
        var zones = bundle.Zones.Select(z => z.Key).ToHashSet(StringComparer.Ordinal);
        var items = bundle.ItemTemplates.Select(i => i.Key).ToHashSet(StringComparer.Ordinal);
        var mobs = bundle.MobTemplates.Select(m => m.Key).ToHashSet(StringComparer.Ordinal);
        var rooms = bundle.Rooms.Select(r => r.Key).ToHashSet(StringComparer.Ordinal);

        CheckZones(bundle, worlds, Error, Warn);
        CheckRooms(bundle, zones, Error, Warn);

        var edges = CollectExits(bundle, rooms, items, Error, Warn);

        CheckReciprocity(edges, rooms, Warn);
        CheckConnectivity(edges, rooms, Error);
        CheckSpawners(bundle, zones, rooms, mobs, items, Error, Warn);
        CheckMobs(bundle, items, Error, Warn);
        CheckQuests(bundle, mobs, items, Error, Warn);
        CheckTerrain(bundle, Error, Warn);
        CheckFlags(bundle, Error);

        return new BundleCheck(findings);
    }

    private static void CheckZones(
        WorldBundle bundle,
        HashSet<string> worlds,
        Action<string> error,
        Action<string> warn)
    {
        foreach (var zone in bundle.Zones)
        {
            if (!zone.Key.StartsWith(zone.WorldKey + ".", StringComparison.Ordinal))
            {
                error($"zone {zone.Key} must begin with its world key '{zone.WorldKey}' plus a dot");
            }

            if (!worlds.Contains(zone.WorldKey))
            {
                warn($"zone {zone.Key} names world {zone.WorldKey}, which this bundle does not carry");
            }

            if (zone.MinLevel > zone.MaxLevel)
            {
                error($"zone {zone.Key} has minLevel above maxLevel");
            }
        }
    }

    private static void CheckRooms(
        WorldBundle bundle,
        HashSet<string> zones,
        Action<string> error,
        Action<string> warn)
    {
        foreach (var room in bundle.Rooms)
        {
            // One call in place of a length limit, a segment count and a segment grammar written
            // out again in another language. This is the rule the engine itself parses by.
            if (!RoomKey.TryParse(room.Key, out _))
            {
                error(
                    $"room key {room.Key} is not a RoomKey: exactly three dot-separated segments of "
                    + $"lowercase letters, digits and inner hyphens, at most {RoomKey.MaxLength} characters");
            }

            if (!zones.Contains(room.ZoneKey))
            {
                warn($"room {room.Key} names zone {room.ZoneKey}, which this bundle does not carry");
            }
            else if (!room.Key.StartsWith(room.ZoneKey + ".", StringComparison.Ordinal))
            {
                error($"room {room.Key} declares zone {room.ZoneKey} but does not live in it");
            }
        }
    }

    private readonly record struct Edge(string From, Direction Direction, string To);

    private static HashSet<Edge> CollectExits(
        WorldBundle bundle,
        HashSet<string> rooms,
        HashSet<string> items,
        Action<string> error,
        Action<string> warn)
    {
        var edges = new HashSet<Edge>();

        foreach (var room in bundle.Rooms)
        {
            var directions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var exit in room.Exits)
            {
                if (!directions.Add(exit.Direction))
                {
                    error($"room {room.Key} has two {exit.Direction} exits");
                }

                // The importer's own parser, so a direction this accepts is one the import takes.
                if (!DirectionExtensions.TryParse(exit.Direction, out var direction))
                {
                    error($"room {room.Key} has an unknown direction '{exit.Direction}'");
                    continue;
                }

                if (!rooms.Contains(exit.To))
                {
                    warn($"room {room.Key} exit {exit.Direction} points at {exit.To}, "
                        + "which this bundle does not carry");
                }

                if (!string.IsNullOrEmpty(exit.RequiredItemKey) && !items.Contains(exit.RequiredItemKey))
                {
                    warn($"room {room.Key} exit {exit.Direction} requires item {exit.RequiredItemKey}, "
                        + "which this bundle does not carry");
                }

                edges.Add(new Edge(room.Key, direction, exit.To));
            }
        }

        return edges;
    }

    private static void CheckReciprocity(HashSet<Edge> edges, HashSet<string> rooms, Action<string> warn)
    {
        foreach (var edge in edges.OrderBy(e => e.From, StringComparer.Ordinal).ThenBy(e => e.Direction))
        {
            if (!rooms.Contains(edge.To))
            {
                // Already warned about as a dangling target; saying it twice helps nobody.
                continue;
            }

            var back = new Edge(edge.To, edge.Direction.Opposite(), edge.From);

            if (!edges.Contains(back))
            {
                warn($"one-way exit: {edge.From} --{edge.Direction.ToLowerName()}--> {edge.To} "
                    + $"has no {edge.Direction.Opposite().ToLowerName()} coming back");
            }
        }
    }

    /// <summary>
    /// Anything in its own island is <c>goto</c>-only. Undirected, because a room you can leave but
    /// never reach is just as stranded as one you can reach but never leave.
    /// </summary>
    private static void CheckConnectivity(HashSet<Edge> edges, HashSet<string> rooms, Action<string> error)
    {
        if (rooms.Count == 0)
        {
            return;
        }

        var neighbours = rooms.ToDictionary(
            key => key,
            _ => new HashSet<string>(StringComparer.Ordinal),
            StringComparer.Ordinal);

        foreach (var edge in edges.Where(e => rooms.Contains(e.From) && rooms.Contains(e.To)))
        {
            neighbours[edge.From].Add(edge.To);
            neighbours[edge.To].Add(edge.From);
        }

        var start = rooms.OrderBy(k => k, StringComparer.Ordinal).First();
        var seen = new HashSet<string>(StringComparer.Ordinal) { start };
        var stack = new Stack<string>([start]);

        while (stack.Count > 0)
        {
            foreach (var neighbour in neighbours[stack.Pop()])
            {
                if (seen.Add(neighbour))
                {
                    stack.Push(neighbour);
                }
            }
        }

        foreach (var orphan in rooms.Except(seen).OrderBy(k => k, StringComparer.Ordinal))
        {
            error($"room {orphan} has no path to the rest of the bundle");
        }
    }

    private static void CheckSpawners(
        WorldBundle bundle,
        HashSet<string> zones,
        HashSet<string> rooms,
        HashSet<string> mobs,
        HashSet<string> items,
        Action<string> error,
        Action<string> warn)
    {
        var seen = new HashSet<Guid>();

        foreach (var spawner in bundle.Spawners)
        {
            // Spawner ids are content: re-minting one doubles a zone's population, and two
            // spawners sharing one means the second silently replaces the first on import.
            if (!seen.Add(spawner.Id))
            {
                error($"two spawners share the id {spawner.Id}; re-importing would double the population");
            }

            if (!zones.Contains(spawner.ZoneKey))
            {
                warn($"spawner {spawner.Id} names zone {spawner.ZoneKey}, which this bundle does not carry");
            }

            var known = spawner.TemplateKind == TemplateKind.Mob ? mobs : items;

            if (!known.Contains(spawner.TemplateKey))
            {
                warn($"spawner {spawner.Id} places {spawner.TemplateKey}, which this bundle does not carry");
            }

            if (spawner.TemplateKind == TemplateKind.Item && spawner.FightsAtLevel is not null)
            {
                error($"spawner {spawner.Id} is an item spawner with fightsAtLevel set");
            }

            foreach (var roomKey in spawner.RoomKeys.Where(key => !rooms.Contains(key)))
            {
                warn($"spawner {spawner.Id} places into room {roomKey}, which this bundle does not carry");
            }
        }
    }

    private static void CheckMobs(
        WorldBundle bundle,
        HashSet<string> items,
        Action<string> error,
        Action<string> warn)
    {
        foreach (var mob in bundle.MobTemplates)
        {
            var behavior = mob.Behavior ?? [];

            foreach (var stocked in MobBehavior.SellsOf(behavior).Where(key => !items.Contains(key)))
            {
                warn($"{mob.Key} sells {stocked}, which this bundle does not carry");
            }

            if (MobBehavior.IsShopkeeper(behavior) && MobBehavior.SellsOf(behavior).Count == 0)
            {
                warn($"{mob.Key} is flagged shopkeeper but stocks nothing");
            }

            foreach (var key in behavior.Keys.Where(k => !MobBehavior.KnownKeys.Contains(k)).Order(StringComparer.Ordinal))
            {
                error($"{mob.Key} has behavior key '{key}', which the engine does not read; "
                    + $"it reads {string.Join(", ", MobBehavior.KnownKeys.Order(StringComparer.Ordinal))}");
            }
        }
    }

    private static void CheckQuests(
        WorldBundle bundle,
        HashSet<string> mobs,
        HashSet<string> items,
        Action<string> error,
        Action<string> warn)
    {
        foreach (var quest in bundle.Quests)
        {
            foreach (var (value, known, label) in new[]
            {
                (quest.GiverMobKey, mobs, "giver"),
                (quest.TurninMobKey, mobs, "turn-in"),
                (quest.RequiredItemKey, items, "required item"),
                (quest.RewardItemKey, items, "reward item"),
            })
            {
                if (!string.IsNullOrEmpty(value) && !known.Contains(value))
                {
                    warn($"quest {quest.Key} names {label} {value}, which this bundle does not carry");
                }
            }

            // An error rather than a warning: there is no reading of an unread dialogue key under
            // which the content works. The line is authored, stored, exported, re-imported, and
            // never spoken - which is exactly what happened to all 35 quests (BUGS.md #6).
            foreach (var key in (quest.Dialogue ?? [])
                .Keys
                .Where(k => !QuestDialogue.All.Contains(k))
                .Order(StringComparer.Ordinal))
            {
                error($"quest {quest.Key} has dialogue key '{key}', which the engine does not read; "
                    + $"it reads {string.Join(", ", QuestDialogue.All.Order(StringComparer.Ordinal))}");
            }
        }
    }

    /// <summary>
    /// All of these are silent at runtime: an unlisted character draws a tile the legend cannot
    /// explain, a ragged grid renders as a ragged room, and a room with nowhere to stand renders
    /// with its occupants missing entirely.
    /// </summary>
    private static void CheckTerrain(WorldBundle bundle, Action<string> error, Action<string> warn)
    {
        foreach (var room in bundle.Rooms)
        {
            var grid = room.Grid ?? [];
            var legend = room.Legend ?? new Dictionary<string, string>();

            if (grid.Count == 0)
            {
                if (legend.Count > 0)
                {
                    warn($"room {room.Key} has a legend and no grid");
                }

                continue;
            }

            var width = grid[0].Length;

            if (grid.Any(row => row.Length != width))
            {
                error($"room {room.Key} has rows of differing length");
                continue;
            }

            var used = grid.SelectMany(row => row).Select(ch => ch.ToString()).ToHashSet(StringComparer.Ordinal);

            foreach (var glyph in used.Except(legend.Keys).Order(StringComparer.Ordinal))
            {
                error($"room {room.Key} draws '{glyph}' with nothing in its legend");
            }

            foreach (var glyph in legend.Keys.Except(used).Order(StringComparer.Ordinal))
            {
                warn($"room {room.Key} legends '{glyph}' and never draws it");
            }

            var open = grid
                .SelectMany(row => row)
                .Count(ch => legend.TryGetValue(ch.ToString(), out var tile)
                    && !RoomLayoutService.NonPlaceable.Contains(tile));

            if (open < MinOpenCells)
            {
                error($"room {room.Key} leaves {open} cells to stand on, under the {MinOpenCells} minimum");
            }
        }
    }

    private static void CheckFlags(WorldBundle bundle, Action<string> error)
    {
        var known = RoomFlags.All.Select(f => f.Key).ToHashSet(StringComparer.Ordinal);

        foreach (var (flags, label, key) in bundle.Worlds
            .Select(w => (w.Flags, "world", w.Key))
            .Concat(bundle.Zones.Select(z => (z.Flags, "zone", z.Key)))
            .Concat(bundle.Rooms.Select(r => (r.Flags, "room", r.Key))))
        {
            if (flags.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                continue;
            }

            foreach (var flag in flags.EnumerateObject().Where(f => !known.Contains(f.Name)))
            {
                error($"{label} {key} sets '{flag.Name}', which is not in the RoomFlags registry");
            }
        }
    }
}
