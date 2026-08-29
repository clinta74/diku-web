#:project ../src/DikuWeb.Server/DikuWeb.Server.csproj
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:property PublishAot=false

// Draws a realm's map from the authored bundles (docs/MAP-RENDERING.md).
//
//     dotnet run tools/render-map.cs -- --realm ossara -o content/map/ossara.svg
//     dotnet run tools/render-map.cs -- --realm ossara --model      # the derived model, no drawing
//
// **Nothing here is authored.** Room roles, road runs, building frontage and the vertical-exit
// treatment are all derived from what is already in `content/` — see MAP-RENDERING.md §3.2. The
// art sidecar at `content/map/<realm>.json` overrides that derivation and never supplies it, so
// deleting the sidecar must still produce a complete map.
//
// **Every random choice is seeded from a room key**, exactly as the terrain is, so re-running on
// unchanged content produces a byte-identical file and a regenerated map is a readable diff.

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DikuWeb.Server.Building;

var realm = "ossara";
var output = (string?)null;
var modelOnly = false;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--realm" when i + 1 < args.Length:
            realm = args[++i];
            break;

        case "-o" or "--out" when i + 1 < args.Length:
            output = args[++i];
            break;

        case "--model":
            modelOnly = true;
            break;

        default:
            Console.Error.WriteLine($"unexpected argument '{args[i]}'");
            Console.Error.WriteLine("usage: render-map.cs -- --realm <key> [-o <file.svg>] [--model]");
            return 2;
    }
}

var repo = FindRepoRoot();

if (repo is null)
{
    Console.Error.WriteLine("could not find the repository root (no content/ directory above cwd).");
    return 1;
}

var world = World.Load(repo, realm);

if (world.Rooms.Count == 0)
{
    Console.Error.WriteLine($"no rooms found for realm '{realm}' under content/{realm}/.");
    return 1;
}

var sidecar = Sidecar.Load(Path.Combine(repo, "content", "map", $"{realm}.json"));
var model = MapModel.Derive(world, sidecar);

foreach (var warning in model.Warnings)
{
    Console.Error.WriteLine($"warning: {warning}");
}

if (modelOnly)
{
    Console.WriteLine(model.ToJson());
    return 0;
}

var svg = Renderer.Draw(model);

if (output is null)
{
    Console.WriteLine(svg);
}
else
{
    var folder = Path.GetDirectoryName(Path.GetFullPath(output));

    if (!string.IsNullOrEmpty(folder))
    {
        Directory.CreateDirectory(folder);
    }

    // No BOM, so the file diffs like the text it is.
    File.WriteAllText(output, svg, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    Console.Error.WriteLine($"{output}: {model.Rooms.Count} rooms, {model.Runs.Count} runs, {model.Markers.Count} markers, {model.Borders.Count} borders.");
}

return 0;

static string? FindRepoRoot()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());

    while (dir is not null)
    {
        if (Directory.Exists(Path.Combine(dir.FullName, "content")))
        {
            return dir.FullName;
        }

        dir = dir.Parent;
    }

    return null;
}

// ---------------------------------------------------------------------------------------------
// Layer 0 — the bundles, read and never written.
// ---------------------------------------------------------------------------------------------

sealed record Exit(string Direction, string To);

sealed record Room(
    string Key,
    string ZoneKey,
    string Title,
    string Description,
    HashSet<string> Flags,
    IReadOnlyList<string> Grid,
    IReadOnlyDictionary<string, string> Legend,
    int EditorX,
    int EditorY,
    IReadOnlyList<Exit> Exits)
{
    public string Realm => Key.Split('.')[0];
}

sealed record Zone(string Key, string Name, int MinLevel, int MaxLevel);

sealed record Shop(string Keeper, IReadOnlyList<string> Sells);

sealed class World
{
    public required string Realm { get; init; }

    public required string Name { get; init; }

    public required Dictionary<string, Room> Rooms { get; init; }

    public required Dictionary<string, Zone> Zones { get; init; }

    /// <summary>Room key -> the shopkeeper standing in it, if any.</summary>
    public required Dictionary<string, Shop> Shops { get; init; }

    public static World Load(string repo, string realm)
    {
        var dir = Path.Combine(repo, "content", realm);

        if (!Directory.Exists(dir))
        {
            return Empty(realm);
        }

        var rooms = new Dictionary<string, Room>(StringComparer.Ordinal);
        var zones = new Dictionary<string, Zone>(StringComparer.Ordinal);
        var mobs = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
        var spawners = new List<JsonNode>();
        var name = realm;

        // A realm's files overlap — every room in gatetown.json is in the-reaches.json too. Keyed
        // merge rather than concatenation, so the overlap is a no-op instead of a duplicate.
        foreach (var file in Directory.EnumerateFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            var doc = JsonNode.Parse(File.ReadAllText(file))!.AsObject();

            foreach (var w in doc["worlds"]?.AsArray() ?? [])
            {
                if (Str(w?["key"]) == realm)
                {
                    name = Str(w?["name"]) ?? realm;
                }
            }

            foreach (var z in doc["zones"]?.AsArray() ?? [])
            {
                if (z is null || Str(z["key"]) is not { } zoneKey)
                {
                    continue;
                }

                zones[zoneKey] = new Zone(zoneKey, Str(z["name"]) ?? zoneKey, Int(z["minLevel"]), Int(z["maxLevel"]));
            }

            foreach (var r in doc["rooms"]?.AsArray() ?? [])
            {
                if (r is null || Str(r["key"]) is not { } roomKey)
                {
                    continue;
                }

                rooms[roomKey] = ReadRoom(roomKey, r);
            }

            foreach (var mob in doc["mobTemplates"]?.AsArray() ?? [])
            {
                if (mob is not null && Str(mob["key"]) is { } mobKey)
                {
                    mobs[mobKey] = mob.DeepClone();
                }
            }

            foreach (var spawner in doc["spawners"]?.AsArray() ?? [])
            {
                if (spawner is not null)
                {
                    spawners.Add(spawner.DeepClone());
                }
            }
        }

        return new World
        {
            Realm = realm,
            Name = name,
            Rooms = rooms,
            Zones = zones,
            Shops = ReadShops(mobs, spawners),
        };
    }

    static Room ReadRoom(string key, JsonNode r)
    {
        var flags = new HashSet<string>(StringComparer.Ordinal);

        foreach (var f in r["flags"]?.AsObject() ?? [])
        {
            if (f.Value is JsonValue v && v.TryGetValue<bool>(out var on) && on)
            {
                flags.Add(f.Key);
            }
        }

        var legend = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var l in r["legend"]?.AsObject() ?? [])
        {
            legend[l.Key] = Str(l.Value) ?? "";
        }

        var exits = new List<Exit>();

        foreach (var e in r["exits"]?.AsArray() ?? [])
        {
            if (Str(e?["to"]) is { } to && Str(e?["direction"]) is { } direction)
            {
                exits.Add(new Exit(direction, to));
            }
        }

        var grid = (r["grid"]?.AsArray() ?? []).Select(g => Str(g) ?? "").ToList();

        return new Room(
            key,
            Str(r["zoneKey"]) ?? "",
            Str(r["title"]) ?? key,
            Str(r["description"]) ?? "",
            flags,
            grid,
            legend,
            Int(r["editorX"]),
            Int(r["editorY"]),
            exits);
    }

    /// <summary>
    /// Who keeps shop where. A shopkeeper is the strongest "this room is a premises" signal in the
    /// bundle, and the <c>sells</c> list is what picks the sign glyph (§4.3).
    /// </summary>
    static Dictionary<string, Shop> ReadShops(Dictionary<string, JsonNode> mobs, List<JsonNode> spawners)
    {
        var shops = new Dictionary<string, Shop>(StringComparer.Ordinal);

        foreach (var s in spawners)
        {
            if (Str(s["templateKind"]) != "Mob" || Str(s["templateKey"]) is not { } templateKey)
            {
                continue;
            }

            if (!mobs.TryGetValue(templateKey, out var mob))
            {
                continue;
            }

            var behavior = mob["behavior"]?.AsObject();

            // A wandering mob is not evidence about any one room.
            if (Bool(s["wanders"]) || Bool(behavior?["wanders"]) || !Bool(behavior?["shopkeeper"]))
            {
                continue;
            }

            var sells = (behavior?["sells"]?.AsArray() ?? [])
                .Select(x => Str(x) ?? "")
                .Where(x => x.Length > 0)
                .ToList();

            foreach (var rk in s["roomKeys"]?.AsArray() ?? [])
            {
                if (Str(rk) is { } roomKey)
                {
                    shops[roomKey] = new Shop(Str(mob["name"]) ?? templateKey, sells);
                }
            }
        }

        return shops;
    }

    static World Empty(string realm) => new()
    {
        Realm = realm,
        Name = realm,
        Rooms = new Dictionary<string, Room>(StringComparer.Ordinal),
        Zones = new Dictionary<string, Zone>(StringComparer.Ordinal),
        Shops = new Dictionary<string, Shop>(StringComparer.Ordinal),
    };

    internal static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    internal static int Int(JsonNode? n) => n is JsonValue v && v.TryGetValue<int>(out var i) ? i : 0;

    internal static bool Bool(JsonNode? n) => n is JsonValue v && v.TryGetValue<bool>(out var b) && b;
}

// ---------------------------------------------------------------------------------------------
// Layer 2 — the art sidecar. Every field optional; it overrides, and it never supplies.
// ---------------------------------------------------------------------------------------------

sealed class RoomHint
{
    public string? Role { get; init; }

    public string? Label { get; init; }

    public string? Vertical { get; init; }

    public string? Glyph { get; init; }

    public int[]? Footprint { get; init; }
}

sealed class ZoneHint
{
    public double? Pitch { get; init; }

    public string? Density { get; init; }
}

sealed class Sidecar
{
    public string? Title { get; init; }

    public string? Subtitle { get; init; }

    public Dictionary<string, RoomHint> Rooms { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, ZoneHint> Zones { get; } = new(StringComparer.Ordinal);

    public static Sidecar Load(string path)
    {
        if (!File.Exists(path))
        {
            return new Sidecar();
        }

        var doc = JsonNode.Parse(File.ReadAllText(path))!.AsObject();

        var sidecar = new Sidecar
        {
            Title = World.Str(doc["title"]),
            Subtitle = World.Str(doc["subtitle"]),
        };

        foreach (var r in doc["rooms"]?.AsObject() ?? [])
        {
            if (r.Value?.AsObject() is not { } o)
            {
                continue;
            }

            var footprint = (o["footprint"]?.AsArray() ?? []).Select(World.Int).ToArray();

            sidecar.Rooms[r.Key] = new RoomHint
            {
                Role = World.Str(o["role"]),
                Label = World.Str(o["label"]),
                Vertical = World.Str(o["vertical"]),
                Glyph = World.Str(o["glyph"]),
                Footprint = footprint.Length == 2 ? footprint : null,
            };
        }

        foreach (var z in doc["zones"]?.AsObject() ?? [])
        {
            if (z.Value?.AsObject() is not { } o)
            {
                continue;
            }

            sidecar.Zones[z.Key] = new ZoneHint
            {
                Pitch = o["pitch"] is JsonValue pv && pv.TryGetValue<double>(out var pitch) ? pitch : null,
                Density = World.Str(o["density"]),
            };
        }

        return sidecar;
    }
}
// Layer 1 — derived. Everything below is a function of the bundles, MAP-RENDERING.md §3.2.
// ---------------------------------------------------------------------------------------------

enum Role { Road, Track, Building, Plaza, Open, Edge, Ruin }

enum Vertical { None, Marker, Breakout, OwnSheet, Border }

sealed class PlacedRoom
{
    public required Room Room { get; init; }
    public required string Kind { get; init; }
    public Role Role { get; set; }
    public int Degree { get; set; }

    /// <summary>Cell position in the realm-wide grid (§1.1). Meaningless for markers.</summary>
    public int GridX { get; set; }
    public int GridY { get; set; }

    /// <summary>Absolute drawing position, centre of the cell.</summary>
    public double X { get; set; }
    public double Y { get; set; }
    public double Pitch { get; set; } = 120;

    public Shop? Shop { get; set; }
    public string Label { get; set; } = "";

    /// <summary>Set when this room is drawn as a glyph on another room instead of a cell.</summary>
    public MarkerInfo? AsMarker { get; set; }

    public bool Drawn => AsMarker is null;
}

sealed class MarkerInfo
{
    public required string HostKey { get; init; }
    public required string Glyph { get; init; }
    public required string Direction { get; init; }
}

sealed class Marker
{
    public required PlacedRoom Host { get; init; }
    public required string Glyph { get; init; }
    public required string Label { get; init; }
}

sealed class Border
{
    public required PlacedRoom From { get; init; }
    public required string Caption { get; init; }
    public required string Direction { get; init; }
}

sealed class Run
{
    public required List<PlacedRoom> Rooms { get; init; }
    public required bool Horizontal { get; init; }
    public string? Label { get; set; }
}

sealed class ZoneLayout
{
    public required Zone Zone { get; init; }
    public string Density { get; set; } = "settled";
    public double Pitch { get; set; } = 120;
    public int OffX { get; set; }
    public int OffY { get; set; }
    public double OriginX { get; set; }
    public double OriginY { get; set; }
    /// <summary>Placement order from the seam walk. A later block is the one that gives way.</summary>
    public int Order { get; set; }

    public int MinX { get; set; }
    public int MinY { get; set; }
    public List<PlacedRoom> Rooms { get; } = [];

    /// <summary>
    /// Empty cells inside a town that front onto a way (§4.4). They carry the unlabelled buildings,
    /// and they are ground: a filler block that is not part of the land mass floats off the town.
    /// </summary>
    public List<(int X, int Y)> FillerCells { get; } = [];
}

sealed class MapModel
{
    public required World World { get; init; }
    public required Sidecar Sidecar { get; init; }
    public required Dictionary<string, PlacedRoom> Rooms { get; init; }
    public required Dictionary<string, ZoneLayout> Zones { get; init; }
    public List<Run> Runs { get; } = [];
    public List<Marker> Markers { get; } = [];
    public List<Border> Borders { get; } = [];
    public List<string> Warnings { get; } = [];

    /// <summary>
    /// Whether this realm is mostly <c>standing</c> — floor with void all round it and nothing
    /// underneath (WORLD.md §10.1). The Unlit is, and it wants the sheet the other way up: pale
    /// floors adrift in the dark, not a torn continent on cream paper. Drawn like everywhere else
    /// it decorates the idea instead of making it.
    /// </summary>
    public bool Adrift { get; private set; }
    public double MinX, MinY, MaxX, MaxY;

    internal static readonly string[] Lateral = ["north", "south", "east", "west"];

    internal static (int dx, int dy) Step(string dir) => dir switch
    {
        "north" => (0, -1),
        "south" => (0, 1),
        "east" => (1, 0),
        "west" => (-1, 0),
        _ => (0, 0),
    };

    public static MapModel Derive(World world, Sidecar sidecar)
    {
        var m = new MapModel
        {
            World = world,
            Sidecar = sidecar,
            Rooms = new Dictionary<string, PlacedRoom>(StringComparer.Ordinal),
            Zones = new Dictionary<string, ZoneLayout>(StringComparer.Ordinal),
        };

        m.Classify();
        m.ClassifyVerticals();
        m.Stitch();
        m.ChooseDensity();
        m.Place();
        m.BuildRuns();
        m.FindFiller();
        m.Adrift = m.Rooms.Values.Count(r => r.Kind == "standing") > m.Rooms.Count * 0.35;
        return m;
    }

    // -- §1.2 terrain kind, §3.2 role ----------------------------------------------------------

    void Classify()
    {
        foreach (var room in World.Rooms.Values)
        {
            var kind = RecoverKind(room);
            var degree = room.Exits.Count(e => Lateral.Contains(e.Direction));
            var shop = World.Shops.GetValueOrDefault(room.Key);
            var hint = Sidecar.Rooms.GetValueOrDefault(room.Key);

            var role = DeriveRole(room, kind, degree, shop is not null);
            if (hint?.Role is { } forced && Enum.TryParse<Role>(forced, ignoreCase: true, out var parsed))
            {
                role = parsed;
            }

            Rooms[room.Key] = new PlacedRoom
            {
                Room = room,
                Kind = kind,
                Role = role,
                Degree = degree,
                Shop = shop,
                Label = hint?.Label ?? room.Title,
            };
        }
    }

    static Role DeriveRole(Room room, string kind, int degree, bool hasShop)
    {
        // 1 — a shopkeeper standing in a room makes it a premises whatever its degree. Wick's Yard
        // is the general store with three exits on it and is still a shop.
        if (hasShop || room.Flags.Contains("indoors"))
        {
            return Role.Building;
        }

        return kind switch
        {
            "hall" or "taproom" or "store" or "shrine" => degree >= 3 ? Role.Plaza : Role.Building,
            "street" or "market" => Role.Road,
            "gateroad" or "waste" => Role.Track,
            "rim" or "brink" or "standing" => Role.Edge,
            "ruin" or "collapse" => Role.Ruin,
            _ => Role.Open,
        };
    }

    /// <summary>
    /// Recovers the terrain kind a room was generated from (§1.2).
    /// </summary>
    /// <remarks>
    /// The tile-set narrows it; the round-trip decides it. Generation is deterministic on the room
    /// key, so regenerating under each candidate and comparing the grid identifies the kind
    /// exactly — no heuristic, and nothing to store that was already implied.
    /// </remarks>
    string RecoverKind(Room room)
    {
        var used = room.Legend.Values.ToHashSet(StringComparer.Ordinal);

        // Both directions of the test matter. Subset alone lets `collapse` explain a room with
        // only {floor, rock} in it, and an Enclosed kind always draws a wall border — so a room
        // with no wall tile is not enclosed, whatever its other tiles allow. Requiring the base
        // and the feature to be *present* takes Ossara's wilderness off the ruin pile.
        var candidates = TerrainGenerator.Kinds
            .Where(k => used.IsSubsetOf(TilesOf(k)) && used.Contains(k.Base) && used.Contains(k.Feature))
            .Select(k => k.Key)
            .ToList();

        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        foreach (var candidate in candidates)
        {
            if (TerrainGenerator.Generate(candidate, room.Key).Grid.SequenceEqual(room.Grid))
            {
                return candidate;
            }
        }

        // The kinds are themselves a reconstruction (TerrainGenerator's own remarks say so), so a
        // room drawn from a kind that was never reconstructed cannot round-trip. Say which, rather
        // than silently taking the first candidate and drawing a meadow as a collapsed hall.
        Warnings.Add(candidates.Count == 0
            ? $"{room.Key}: no terrain kind explains {{{string.Join(", ", used.Order(StringComparer.Ordinal))}}}; drawn as open ground."
            : $"{room.Key}: {{{string.Join(", ", used.Order(StringComparer.Ordinal))}}} fits {string.Join('/', candidates)} and matches none of them byte-for-byte; taking {candidates[0]}.");

        return candidates.FirstOrDefault() ?? "open";
    }

    static HashSet<string> TilesOf(TerrainKind k)
    {
        var set = new HashSet<string>(StringComparer.Ordinal) { k.Base, k.Feature };

        if (k.Furniture is not null)
        {
            set.Add(k.Furniture);
        }

        foreach (var d in (k.Debris ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            set.Add(d);
        }

        return set;
    }

    // -- §4.6 vertical exits -------------------------------------------------------------------

    void ClassifyVerticals()
    {
        foreach (var room in World.Rooms.Values.OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            foreach (var exit in room.Exits)
            {
                if (exit.Direction is not ("up" or "down"))
                {
                    continue;
                }

                // §4.6.3 — `up` is overloaded. All four realm gates use it on both sides, so a
                // cross-realm exit is detected by the target key and is never vertical movement.
                if (!World.Rooms.TryGetValue(exit.To, out var target))
                {
                    Borders.Add(new Border
                    {
                        From = Rooms[room.Key],
                        Caption = "To " + Title(exit.To.Split('.')[0]),
                        Direction = exit.Direction,
                    });
                    continue;
                }

                if (target.Realm != room.Realm)
                {
                    Borders.Add(new Border
                    {
                        From = Rooms[room.Key],
                        Caption = "To " + Title(target.Realm),
                        Direction = exit.Direction,
                    });
                    continue;
                }

                var beyond = SideBeyond(room.Key, exit.To);

                if (beyond == 0)
                {
                    // Not a bridge: you can get there without this step, so it is a shortcut
                    // between two rooms that both stand on the map already.
                    continue;
                }

                // The same link is written from both ends. Only the end with less of the world
                // behind it is the level — from the other end this is the way back up.
                if (beyond * 2 > World.Rooms.Count)
                {
                    continue;
                }

                var hint = Sidecar.Rooms.GetValueOrDefault(exit.To);
                var treatment = hint?.Vertical switch
                {
                    "marker" => Vertical.Marker,
                    "breakout" => Vertical.Breakout,
                    "sheet" => Vertical.OwnSheet,
                    _ => beyond == 1 ? Vertical.Marker : beyond <= 8 ? Vertical.Breakout : Vertical.OwnSheet,
                };

                if (treatment != Vertical.Marker)
                {
                    Warnings.Add($"{room.Key} {exit.Direction} -> {exit.To}: wants a {treatment} ({beyond} rooms beyond it), which is not built; drawn as a marker instead.");
                }

                // Marker rooms are held out of the grid entirely (§4.6.1). The editor grid cannot
                // tell "west of" from "below", and The Root Cellar is authored west of its mouth —
                // trusting its cell would draw a cellar as a building on the street.
                Rooms[exit.To].AsMarker = new MarkerInfo
                {
                    HostKey = room.Key,
                    Glyph = hint?.Glyph ?? GlyphFor(target, exit.Direction, Rooms[exit.To].Kind),
                    Direction = exit.Direction,
                };
            }
        }

        foreach (var pr in Rooms.Values.Where(p => p.AsMarker is not null).OrderBy(p => p.Room.Key, StringComparer.Ordinal))
        {
            var host = Rooms[pr.AsMarker!.HostKey];

            if (host.AsMarker is not null)
            {
                continue;
            }

            Markers.Add(new Marker { Host = host, Glyph = pr.AsMarker.Glyph, Label = pr.Label });
        }
    }

    static string GlyphFor(Room target, string direction, string kind) =>
        target.Flags.Contains("dark") && direction == "down" ? "cellar"
        : kind is "ruin" or "collapse" ? "cave"
        : kind is "standing" ? "portal"
        : direction == "down" ? "cave"
        : "stair";

    /// <summary>
    /// How much of the world lies beyond <paramref name="b"/> once the link is cut (§4.6). Zero
    /// when the link is not a bridge — the far side is reachable anyway, so it is a shortcut
    /// rather than a level.
    /// </summary>
    /// <remarks>
    /// Directional on purpose. A vertical pair is written from both ends, and cutting the cellar
    /// link gives 1 looking down and 237 looking up. Only the end that is actually the small side
    /// becomes a marker; the other reading is the same link seen from the bottom of the stair.
    /// </remarks>
    int SideBeyond(string a, string b)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { b };
        var queue = new Queue<string>();
        queue.Enqueue(b);

        while (queue.Count > 0)
        {
            var k = queue.Dequeue();

            foreach (var n in Adjacent(k))
            {
                if ((k == b && n == a) || (k == a && n == b))
                {
                    continue;   // the cut edge, in whichever direction it is written
                }

                if (seen.Add(n))
                {
                    queue.Enqueue(n);
                }
            }
        }

        return seen.Contains(a) ? 0 : seen.Count;
    }

    Dictionary<string, List<string>>? adjacency;

    IEnumerable<string> Adjacent(string key)
    {
        if (adjacency is null)
        {
            adjacency = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            foreach (var r in World.Rooms.Values)
            {
                foreach (var e in r.Exits)
                {
                    if (!World.Rooms.ContainsKey(e.To))
                    {
                        continue;
                    }

                    Link(r.Key, e.To);
                    Link(e.To, r.Key);
                }
            }

            void Link(string from, string to)
            {
                if (!adjacency!.TryGetValue(from, out var list))
                {
                    adjacency[from] = list = [];
                }

                if (!list.Contains(to))
                {
                    list.Add(to);
                }
            }
        }

        return adjacency.GetValueOrDefault(key) ?? [];
    }

    // -- §1.1 the seam stitch ------------------------------------------------------------------

    void Stitch()
    {
        foreach (var z in World.Zones.Values)
        {
            Zones[z.Key] = new ZoneLayout { Zone = z };
        }

        foreach (var pr in Rooms.Values.Where(p => p.Drawn).OrderBy(p => p.Room.Key, StringComparer.Ordinal))
        {
            if (Zones.TryGetValue(pr.Room.ZoneKey, out var zl))
            {
                zl.Rooms.Add(pr);
            }
        }

        // The realm's zones form a tree joined by seams. BFS from the biggest, and let each seam
        // pin the next zone's grid to one already placed.
        var start = Zones.Values
            .Where(z => z.Rooms.Count > 0)
            .OrderByDescending(z => z.Rooms.Count)
            .ThenBy(z => z.Zone.Key, StringComparer.Ordinal)
            .First();

        var placed = new HashSet<string>(StringComparer.Ordinal) { start.Zone.Key };
        var queue = new Queue<ZoneLayout>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var zone = queue.Dequeue();

            foreach (var pr in zone.Rooms.OrderBy(p => p.Room.Key, StringComparer.Ordinal))
            {
                foreach (var e in pr.Room.Exits)
                {
                    if (!Lateral.Contains(e.Direction))
                    {
                        continue;
                    }

                    if (!Rooms.TryGetValue(e.To, out var target) || !target.Drawn)
                    {
                        continue;
                    }

                    if (target.Room.ZoneKey == zone.Zone.Key || !Zones.TryGetValue(target.Room.ZoneKey, out var tz))
                    {
                        continue;
                    }

                    var (dx, dy) = Step(e.Direction);
                    var offX = pr.Room.EditorX + zone.OffX + dx - target.Room.EditorX;
                    var offY = pr.Room.EditorY + zone.OffY + dy - target.Room.EditorY;

                    if (placed.Contains(tz.Zone.Key))
                    {
                        if (tz.OffX != offX || tz.OffY != offY)
                        {
                            Warnings.Add($"seam conflict: {pr.Room.Key} -> {e.To} puts {tz.Zone.Key} at ({offX},{offY}), but another seam already put it at ({tz.OffX},{tz.OffY}).");
                        }

                        continue;
                    }

                    tz.OffX = offX;
                    tz.OffY = offY;
                    placed.Add(tz.Zone.Key);
                    queue.Enqueue(tz);
                }
            }
        }

        foreach (var zl in Zones.Values.Where(z => z.Rooms.Count > 0))
        {
            if (!placed.Contains(zl.Zone.Key))
            {
                Warnings.Add($"{zl.Zone.Key}: no seam reaches it; placed at the origin.");
            }

            foreach (var pr in zl.Rooms)
            {
                pr.GridX = pr.Room.EditorX + zl.OffX;
                pr.GridY = pr.Room.EditorY + zl.OffY;
            }
        }

        // Within a zone, two rooms in one cell is an authoring error. Across zones it is not:
        // a realm whose zones branch can put two of them over the same realm-space cell, which is
        // a fact about the drawing and is dealt with by separating the blocks (§4.2).
        var occupied = new Dictionary<(string, int, int), string>();

        foreach (var pr in Rooms.Values.Where(p => p.Drawn).OrderBy(p => p.Room.Key, StringComparer.Ordinal))
        {
            if (!occupied.TryAdd((pr.Room.ZoneKey, pr.GridX, pr.GridY), pr.Room.Key))
            {
                Warnings.Add($"cell ({pr.GridX},{pr.GridY}) of {pr.Room.ZoneKey} is claimed by both {occupied[(pr.Room.ZoneKey, pr.GridX, pr.GridY)]} and {pr.Room.Key}.");
            }
        }
    }

    // -- §4.2 per-zone pitch -------------------------------------------------------------------

    void ChooseDensity()
    {
        foreach (var zl in Zones.Values.Where(z => z.Rooms.Count > 0))
        {
            var built = zl.Rooms.Count(r => r.Role is Role.Building or Role.Road or Role.Plaza);
            var ratio = (double)built / zl.Rooms.Count;
            var hint = Sidecar.Zones.GetValueOrDefault(zl.Zone.Key);

            zl.Density = hint?.Density ?? (ratio >= 0.5 ? "town" : ratio >= 0.2 ? "settled" : "wild");
            zl.Pitch = hint?.Pitch ?? zl.Density switch { "town" => 130, "settled" => 190, _ => 230 };

            if (zl.Density != "town")
            {
                continue;
            }

            // Open ground in the middle of a town is a square, not a gap. The Common is grass with
            // four streets on it — left as plain open ground it cuts Gatetown's main street in two.
            foreach (var pr in zl.Rooms.Where(r => r.Role == Role.Open && r.Degree >= 3))
            {
                pr.Role = Role.Plaza;
            }
        }
    }

    void Place()
    {
        foreach (var zl in Zones.Values.Where(z => z.Rooms.Count > 0))
        {
            zl.MinX = zl.Rooms.Min(r => r.GridX);
            zl.MinY = zl.Rooms.Min(r => r.GridY);
        }

        // Blocks, not one grid: a zone's cells are its own size, so a seam decides where the next
        // block starts rather than a global cell multiply (§4.2).
        var ordered = Zones.Values
            .Where(z => z.Rooms.Count > 0)
            .OrderByDescending(z => z.Rooms.Count)
            .ThenBy(z => z.Zone.Key, StringComparer.Ordinal)
            .ToList();

        var first = ordered[0];
        var positioned = new HashSet<string>(StringComparer.Ordinal) { first.Zone.Key };
        first.Order = 0;
        ApplyPositions(first);

        var queue = new Queue<ZoneLayout>();
        queue.Enqueue(first);

        while (queue.Count > 0)
        {
            var zl = queue.Dequeue();

            foreach (var pr in zl.Rooms.OrderBy(p => p.Room.Key, StringComparer.Ordinal))
            {
                foreach (var e in pr.Room.Exits)
                {
                    if (!Lateral.Contains(e.Direction))
                    {
                        continue;
                    }

                    if (!Rooms.TryGetValue(e.To, out var target) || !target.Drawn)
                    {
                        continue;
                    }

                    if (target.Room.ZoneKey == zl.Zone.Key)
                    {
                        continue;
                    }

                    if (!Zones.TryGetValue(target.Room.ZoneKey, out var tz) || !positioned.Add(tz.Zone.Key))
                    {
                        continue;
                    }

                    var (dx, dy) = Step(e.Direction);
                    var gap = (zl.Pitch + tz.Pitch) / 2;

                    tz.OriginX = pr.X + (dx * gap) - ((target.GridX - tz.MinX) * tz.Pitch);
                    tz.OriginY = pr.Y + (dy * gap) - ((target.GridY - tz.MinY) * tz.Pitch);
                    tz.Order = positioned.Count;
                    ApplyPositions(tz);
                    queue.Enqueue(tz);
                }
            }
        }

        foreach (var zl in ordered.Where(z => !positioned.Contains(z.Zone.Key)))
        {
            Warnings.Add($"{zl.Zone.Key}: nothing positions it; drawn over the origin.");
            ApplyPositions(zl);
        }

        Separate();

        var drawn = Rooms.Values.Where(p => p.Drawn).ToList();
        MinX = drawn.Min(p => p.X - (p.Pitch * 0.75));
        MaxX = drawn.Max(p => p.X + (p.Pitch * 0.75));
        MinY = drawn.Min(p => p.Y - (p.Pitch * 0.75));
        MaxY = drawn.Max(p => p.Y + (p.Pitch * 0.75));
    }

    /// <summary>
    /// Slides zone blocks apart until none of them overlaps another (§4.2).
    /// </summary>
    /// <remarks>
    /// Ossara never needed this: its four zones are a chain, so following the seams laid them out
    /// in a line. Grask branches — the Cutting has the Landing north of it, Stiltmarsh east, and
    /// the Owing south — and nothing in a seam says how far a branch reaches, so Stiltmarsh and
    /// the Owing come out drawn over one another. Each seam fixes a direction and a contact point,
    /// not an extent, and two branches of a tree can want the same ground.
    ///
    /// Pushing along the axis of least overlap keeps the seam's own direction: a zone entered from
    /// the north stays south of its neighbour, just further south than the seam alone put it.
    /// </remarks>
    void Separate()
    {
        var blocks = Zones.Values
            .Where(z => z.Rooms.Count > 0)
            .OrderBy(z => z.Order)
            .ThenBy(z => z.Zone.Key, StringComparer.Ordinal)
            .ToList();

        var moved = new HashSet<string>(StringComparer.Ordinal);

        for (var pass = 0; pass < 80; pass++)
        {
            var clean = true;

            for (var i = 0; i < blocks.Count; i++)
            {
                for (var j = i + 1; j < blocks.Count; j++)
                {
                    var (a, b) = (blocks[i], blocks[j]);
                    var (ox, oy) = Worst(a, b);

                    if (ox <= 0.5 || oy <= 0.5)
                    {
                        continue;
                    }

                    // b was placed later, so b is the one that gives way. Pushing along the axis
                    // of least overlap preserves the seam's own direction: a zone entered from the
                    // north stays south of its neighbour, only further south than the seam put it.
                    if (oy <= ox)
                    {
                        b.OriginY += Mid(b, true) >= Mid(a, true) ? oy : -oy;
                    }
                    else
                    {
                        b.OriginX += Mid(b, false) >= Mid(a, false) ? ox : -ox;
                    }

                    ApplyPositions(b);
                    moved.Add(b.Zone.Key);
                    clean = false;
                }
            }

            if (clean)
            {
                break;
            }
        }

        foreach (var key in moved.Order(StringComparer.Ordinal))
        {
            Warnings.Add($"{key}: overlapped another zone and was pushed clear; this realm does not lie flat from its seams alone.");
        }

        // Cell against cell, not bounding box against bounding box. A zone is rarely a rectangle,
        // and two L-shaped zones can share a bounding box while no ground is actually contested.
        (double X, double Y) Worst(ZoneLayout a, ZoneLayout b)
        {
            var (worstX, worstY) = (0.0, 0.0);
            var reach = (a.Pitch + b.Pitch) / 2;

            foreach (var (ax, ay) in Cells(a))
            {
                foreach (var (bx, by) in Cells(b))
                {
                    var dx = reach - Math.Abs(ax - bx);
                    var dy = reach - Math.Abs(ay - by);

                    if (dx <= 0 || dy <= 0)
                    {
                        continue;
                    }

                    worstX = Math.Max(worstX, dx);
                    worstY = Math.Max(worstY, dy);
                }
            }

            return (worstX, worstY);
        }

        static IEnumerable<(double X, double Y)> Cells(ZoneLayout z) =>
            z.Rooms.Select(r => (r.X, r.Y))
                .Concat(z.FillerCells.Select(c => (
                    X: z.OriginX + ((c.X - z.MinX) * z.Pitch),
                    Y: z.OriginY + ((c.Y - z.MinY) * z.Pitch))));

        static double Mid(ZoneLayout z, bool vertical) =>
            vertical
                ? (z.Rooms.Min(r => r.Y) + z.Rooms.Max(r => r.Y)) / 2
                : (z.Rooms.Min(r => r.X) + z.Rooms.Max(r => r.X)) / 2;
    }

    void ApplyPositions(ZoneLayout zl)
    {
        foreach (var pr in zl.Rooms)
        {
            pr.Pitch = zl.Pitch;
            pr.X = zl.OriginX + ((pr.GridX - zl.MinX) * zl.Pitch);
            pr.Y = zl.OriginY + ((pr.GridY - zl.MinY) * zl.Pitch);
        }
    }

    // -- §3.2 road runs ------------------------------------------------------------------------

    void BuildRuns()
    {
        // A plaza carries a run without naming it. The Gate Yard sits in the middle of Gatetown's
        // east-west street, and leaving it out would cut that street into two one-room stubs.
        var ways = Rooms.Values
            .Where(p => p.Drawn && p.Role is Role.Road or Role.Track or Role.Plaza)
            .OrderBy(p => p.Room.Key, StringComparer.Ordinal)
            .ToList();

        var byCell = ways.ToDictionary(p => (p.Room.ZoneKey, p.GridX, p.GridY));

        foreach (var horizontal in new[] { true, false })
        {
            var back = horizontal ? "west" : "north";
            var forward = horizontal ? "east" : "south";

            foreach (var head in ways)
            {
                if (Linked(head, back, byCell) is not null)
                {
                    continue;   // only start a run at its head
                }

                var chain = new List<PlacedRoom> { head };
                var cursor = head;

                while (Linked(cursor, forward, byCell) is { } next && !chain.Contains(next))
                {
                    chain.Add(next);
                    cursor = next;
                }

                if (chain.Count >= 2)
                {
                    Runs.Add(new Run { Rooms = chain, Horizontal = horizontal });
                }
            }
        }

        // A room names at most one run. Longest first, so the through-road wins over its side lane.
        var taken = new HashSet<string>(StringComparer.Ordinal);

        foreach (var run in Runs.OrderByDescending(r => r.Rooms.Count).ThenBy(r => r.Rooms[0].Room.Key, StringComparer.Ordinal))
        {
            // Open country is named room by room, not road by road — a dashed track across a
            // heath is a direction of travel and does not want a street name lettered along it.
            if (run.Rooms.All(r => Zones[r.Room.ZoneKey].Density == "wild"))
            {
                continue;
            }

            var namer = run.Rooms
                .Where(r => r.Role is not Role.Plaza && !taken.Contains(r.Room.Key))
                .OrderByDescending(r => IsWayName(r.Label))
                .ThenByDescending(r => r.Degree)
                .ThenByDescending(r => r.Label.Length)
                .ThenBy(r => r.Room.Key, StringComparer.Ordinal)
                .FirstOrDefault();

            // Better an unnamed continuation than a street named after a dead-end yard, which is
            // what happens when the through-road has already given its name to the longer run.
            if (namer is null || (!IsWayName(namer.Label) && run.Rooms.Count < 3))
            {
                continue;
            }

            run.Label = namer.Label;
            taken.Add(namer.Room.Key);
        }
    }

    /// <summary>
    /// §4.4 — the empty cells a town packs with unlabelled buildings: inside the zone's own block,
    /// and fronting onto something. Filler against nothing is noise rather than a town.
    /// </summary>
    void FindFiller()
    {
        var byCell = Rooms.Values.Where(p => p.Drawn).ToDictionary(p => (p.Room.ZoneKey, p.GridX, p.GridY));

        foreach (var zl in Zones.Values.Where(z => z.Density == "town" && z.Rooms.Count > 0))
        {
            for (var gy = zl.Rooms.Min(r => r.GridY); gy <= zl.Rooms.Max(r => r.GridY); gy++)
            {
                for (var gx = zl.Rooms.Min(r => r.GridX); gx <= zl.Rooms.Max(r => r.GridX); gx++)
                {
                    if (byCell.ContainsKey((zl.Zone.Key, gx, gy)))
                    {
                        continue;
                    }

                    var fronts = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }
                        .Select(d => byCell.GetValueOrDefault((zl.Zone.Key, gx + d.Item1, gy + d.Item2)))
                        .Count(n => n is not null && n.Role is Role.Road or Role.Plaza or Role.Building);

                    if (fronts > 0)
                    {
                        zl.FillerCells.Add((gx, gy));
                    }
                }
            }
        }
    }

    /// <summary>
    /// The neighbouring way in this direction, but only across a real exit (§3.2). Ossara has nine
    /// pairs of rooms in touching cells with no exit between them — two shops on one block — and
    /// chaining on adjacency alone would weld them into a street.
    /// </summary>
    static PlacedRoom? Linked(PlacedRoom from, string direction, Dictionary<(string ZoneKey, int GridX, int GridY), PlacedRoom> byCell)
    {
        var (dx, dy) = Step(direction);

        if (!byCell.TryGetValue((from.Room.ZoneKey, from.GridX + dx, from.GridY + dy), out var neighbour))
        {
            return null;
        }

        // A road name is a local thing. Left unbounded, Gatetown's main street runs on down
        // through the Terraces and gets named for a farm shelf two zones away.
        if (!string.Equals(neighbour.Room.ZoneKey, from.Room.ZoneKey, StringComparison.Ordinal))
        {
            return null;
        }

        var linked = from.Room.Exits.Any(e => e.To == neighbour.Room.Key)
            || neighbour.Room.Exits.Any(e => e.To == from.Room.Key);

        return linked ? neighbour : null;
    }

    /// <summary>Whether a room's name is the name of a way, which is what a run wants to be called.</summary>
    static bool IsWayName(string title) =>
        new[] { "lane", "row", "road", "street", "path", "way", "track", "walk", "alley", "steps", "bridge", "stair" }
            .Any(w => title.Contains(w, StringComparison.OrdinalIgnoreCase));

    internal static string Title(string key) =>
        string.Join(' ', key.Split('-').Select(w => w.Length == 0 ? w : char.ToUpper(w[0], CultureInfo.InvariantCulture) + w[1..]));

    public string ToJson()
    {
        var rooms = new JsonArray();

        foreach (var pr in Rooms.Values.OrderBy(p => p.Room.Key, StringComparer.Ordinal))
        {
            rooms.Add(new JsonObject
            {
                ["key"] = pr.Room.Key,
                ["title"] = pr.Room.Title,
                ["zone"] = pr.Room.ZoneKey,
                ["kind"] = pr.Kind,
                ["role"] = pr.Role.ToString().ToLowerInvariant(),
                ["degree"] = pr.Degree,
                ["cell"] = pr.Drawn ? new JsonArray(pr.GridX, pr.GridY) : null,
                ["marker"] = pr.AsMarker is null ? null : new JsonObject
                {
                    ["host"] = pr.AsMarker.HostKey,
                    ["glyph"] = pr.AsMarker.Glyph,
                },
                ["shop"] = pr.Shop?.Keeper,
            });
        }

        var zones = new JsonArray();

        foreach (var zl in Zones.Values.Where(z => z.Rooms.Count > 0).OrderBy(z => z.Zone.Key, StringComparer.Ordinal))
        {
            zones.Add(new JsonObject
            {
                ["key"] = zl.Zone.Key,
                ["rooms"] = zl.Rooms.Count,
                ["density"] = zl.Density,
                ["pitch"] = zl.Pitch,
                ["offset"] = new JsonArray(zl.OffX, zl.OffY),
            });
        }

        var runs = new JsonArray();

        foreach (var run in Runs)
        {
            runs.Add(new JsonObject
            {
                ["label"] = run.Label,
                ["axis"] = run.Horizontal ? "east-west" : "north-south",
                ["rooms"] = new JsonArray(run.Rooms.Select(r => (JsonNode)JsonValue.Create(r.Room.Title)!).ToArray()),
            });
        }

        return new JsonObject
        {
            ["realm"] = World.Realm,
            ["zones"] = zones,
            ["runs"] = runs,
            ["markers"] = new JsonArray(Markers.Select(x => (JsonNode)JsonValue.Create($"{x.Label} ({x.Glyph}) on {x.Host.Room.Title}")!).ToArray()),
            ["borders"] = new JsonArray(Borders.Select(b => (JsonNode)JsonValue.Create($"{b.From.Room.Title}: {b.Caption}")!).ToArray()),
            ["rooms"] = rooms,
        }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}

// ---------------------------------------------------------------------------------------------
// The drawing. MAP-RENDERING.md §4.
// ---------------------------------------------------------------------------------------------

/// <summary>
/// A small deterministic RNG, spelled out for the same reason <c>TerrainGenerator</c> spells its
/// out: <c>string.GetHashCode</c> is randomised per process and <c>System.Random</c>'s sequence is
/// not contracted between .NET versions. The map has to regenerate byte-identical or its diff is
/// unreadable, so the arithmetic is owned here.
/// </summary>
sealed class Seeded(string seed)
{
    uint state = Hash(seed);

    static uint Hash(string s)
    {
        var h = 2166136261u;

        foreach (var c in s)
        {
            h ^= c;
            h *= 16777619u;
        }

        return h == 0 ? 1u : h;
    }

    public uint Next()
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }

    public double Unit() => (Next() & 0xFFFFFF) / (double)0x1000000;

    public double Between(double lo, double hi) => lo + (Unit() * (hi - lo));

    public int Int(int lo, int hi) => lo + (int)(Next() % (uint)Math.Max(1, hi - lo));
}

/// <summary>
/// Keeps names off one another.
/// </summary>
/// <remarks>
/// There is no text measurement available here, so widths are estimated from the character count
/// against the font's average advance. That is good enough for collision: the cost of a slight
/// over-estimate is a label nudged further than it needed to be, and the cost of not doing it at
/// all is "The Armourer's Shop" written through "The Weaponwright's Shed".
/// </remarks>
sealed class Labels
{
    readonly List<(double X0, double Y0, double X1, double Y1)> placed = [];

    /// <summary>
    /// Per character, because one average is not enough. A run's name is set in capitals and a
    /// place name is not, and at a single 0.52em "THE GATE ROAD" measures short enough to look
    /// like it fits its road, then reaches the sheet clipped to "HE GATE ROAD".
    /// </summary>
    static double Advance(char c) => c switch
    {
        ' ' => 0.28,
        'I' or 'l' or 'i' or 'j' or 't' or 'f' or 'r' or '\'' or '.' or ',' => 0.34,
        'M' or 'W' => 0.9,
        'm' or 'w' => 0.78,
        >= 'A' and <= 'Z' => 0.68,
        _ => 0.5,
    };

    public static double Width(string text, double size, double tracking = 0) =>
        (text.Sum(Advance) * size) + (Math.Max(0, text.Length - 1) * tracking);

    public bool Free(double cx, double cy, double w, double h)
    {
        var box = (cx - (w / 2), cy - h, cx + (w / 2), cy);

        foreach (var p in placed)
        {
            if (box.Item1 < p.X1 && p.X0 < box.Item3 && box.Item2 < p.Y1 && p.Y0 < box.Item4)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Claims a little more than the name occupies, so neighbours keep a gap.</summary>
    public void Claim(double cx, double cy, double w, double h)
    {
        var pad = h * 0.22;
        placed.Add((cx - (w / 2) - pad, cy - h - (pad / 2), cx + (w / 2) + pad, cy + (pad / 2)));
    }
}

static class Renderer
{
    // Assigned once per process from the map being drawn. The tool renders exactly one sheet per
    // run, so a palette that swaps under the whole renderer is simpler than threading it through
    // every drawing method — and there is no second map in flight to be surprised by it.
    static string Ink = "#2a2118";
    static string Paper = "#f3e9d6";
    static string Land = "#e6d9ba";
    static string LandTown = "#ddcfa9";
    static string RoadFill = "#f8f2e1";
    static string Water = "#a9c4cd";
    static string Green = "#c9cfa4";
    static string Faint = "#8a7c62";

    /// <summary>Turns the sheet inside out for a realm that is mostly void (§4.3).</summary>
    static void Adrift()
    {
        Ink = "#e4dabf";      // every line and letter on the sheet
        Paper = "#17151d";    // the dark, and the halo that keeps names off it
        Land = "#8d8878";     // what floor there is
        LandTown = "#8d8878";
        RoadFill = "#a9a292";
        Water = "#4c5a63";
        Green = "#6c7360";
        Faint = "#9b937d";
    }
    const string Serif = "Georgia, 'Iowan Old Style', 'Times New Roman', serif";

    public static string Draw(MapModel m)
    {
        if (m.Adrift)
        {
            Adrift();
        }

        var labels = new Labels();
        const double margin = 150;
        var x0 = m.MinX - margin;
        var y0 = m.MinY - margin - 210;   // room for the title block
        var w = m.MaxX - m.MinX + (margin * 2);
        var h = m.MaxY - m.MinY + (margin * 2) + 210;

        var s = new StringBuilder();
        s.Append(Inv($"<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" viewBox=\"{x0:0.#} {y0:0.#} {w:0.#} {h:0.#}\" width=\"{w:0.#}\" height=\"{h:0.#}\">\n"));
        s.Append($"<title>{Esc(m.Sidecar.Title ?? m.World.Name)}</title>\n");

        Defs(s);
        s.Append(Inv($"<rect x=\"{x0:0.#}\" y=\"{y0:0.#}\" width=\"{w:0.#}\" height=\"{h:0.#}\" fill=\"{Paper}\"/>\n"));
        s.Append(Inv($"<rect x=\"{x0:0.#}\" y=\"{y0:0.#}\" width=\"{w:0.#}\" height=\"{h:0.#}\" fill=\"url(#grain)\"/>\n"));

        LandMass(s, m);
        Texture(s, m);
        Ways(s, m);
        Filler(s, m);
        Buildings(s, m);

        // Names last and in one pass, so every one of them can dodge the ones already down.
        Sheet(s, m, labels, x0, y0, w, h);
        var lettered = WayLabels(s, m, labels);
        PlaceLabels(s, m, labels, lettered);
        MarkerLabels(s, m, labels);
        BorderLabels(s, m, labels);

        s.Append("</svg>\n");
        return s.ToString();
    }

    // -- defs ----------------------------------------------------------------------------------

    static void Defs(StringBuilder s)
    {
        s.Append("<defs>\n");

        // Paper grain. Cheap, and it is most of the difference between a diagram and a drawing.
        s.Append("<filter id=\"grainf\" x=\"0\" y=\"0\" width=\"100%\" height=\"100%\">");
        s.Append("<feTurbulence type=\"fractalNoise\" baseFrequency=\"0.9\" numOctaves=\"3\" seed=\"7\"/>");
        s.Append("<feColorMatrix type=\"saturate\" values=\"0\"/>");
        s.Append("<feComponentTransfer><feFuncA type=\"linear\" slope=\"0.1\"/></feComponentTransfer>");
        s.Append("</filter>\n");
        s.Append("<pattern id=\"grain\" width=\"400\" height=\"400\" patternUnits=\"userSpaceOnUse\">");
        s.Append("<rect width=\"400\" height=\"400\" filter=\"url(#grainf)\" opacity=\"0.55\"/></pattern>\n");

        s.Append("<pattern id=\"hatch\" width=\"8\" height=\"8\" patternUnits=\"userSpaceOnUse\" patternTransform=\"rotate(45)\">");
        s.Append(Inv($"<line x1=\"0\" y1=\"0\" x2=\"0\" y2=\"8\" stroke=\"{Ink}\" stroke-width=\"1.7\" opacity=\"0.42\"/></pattern>\n"));

        s.Append("<pattern id=\"hatchfill\" width=\"9\" height=\"9\" patternUnits=\"userSpaceOnUse\" patternTransform=\"rotate(45)\">");
        s.Append(Inv($"<line x1=\"0\" y1=\"0\" x2=\"0\" y2=\"9\" stroke=\"{Ink}\" stroke-width=\"1.1\" opacity=\"0.26\"/></pattern>\n"));

        s.Append("<pattern id=\"paved\" width=\"15\" height=\"15\" patternUnits=\"userSpaceOnUse\">");
        s.Append(Inv($"<path d=\"M0 0 H15 M0 0 V15\" stroke=\"{Ink}\" stroke-width=\"0.8\" opacity=\"0.16\"/></pattern>\n"));

        s.Append("</defs>\n");
    }

    // -- §4.3 the land itself ------------------------------------------------------------------

    static void LandMass(StringBuilder s, MapModel m)
    {
        s.Append("<g id=\"land\">\n");

        // A realm adrift has no land mass to union — that is the point of it. Each room is its own
        // island of floor with nothing joining it to the next, and the dark between them is not a
        // gap in the drawing, it is the thing the drawing is about.
        if (m.Adrift)
        {
            foreach (var pr in Ordered(m))
            {
                Island(s, pr);
            }

            s.Append("</g>\n");
            return;
        }

        // One path per zone, so the cells union into a single mass instead of reading as tiles.
        foreach (var zl in m.Zones.Values.Where(z => z.Rooms.Count > 0).OrderBy(z => z.Zone.Key, StringComparer.Ordinal))
        {
            var fill = zl.Density == "town" ? LandTown : Land;
            var d = new StringBuilder();

            var centres = zl.Rooms.OrderBy(p => p.Room.Key, StringComparer.Ordinal).Select(p => (p.X, p.Y))
                .Concat(zl.FillerCells.Select(c => (X: zl.OriginX + ((c.X - zl.MinX) * zl.Pitch), Y: zl.OriginY + ((c.Y - zl.MinY) * zl.Pitch))));

            foreach (var (cx, cy) in centres)
            {
                var p = zl.Pitch;
                var half = p * 0.72;
                var r = p * 0.34;
                d.Append(Inv($"M{cx - half + r:0.#} {cy - half:0.#} h{(half * 2) - (r * 2):0.#} a{r:0.#} {r:0.#} 0 0 1 {r:0.#} {r:0.#} v{(half * 2) - (r * 2):0.#} a{r:0.#} {r:0.#} 0 0 1 {-r:0.#} {r:0.#} h{-((half * 2) - (r * 2)):0.#} a{r:0.#} {r:0.#} 0 0 1 {-r:0.#} {-r:0.#} v{-((half * 2) - (r * 2)):0.#} a{r:0.#} {r:0.#} 0 0 1 {r:0.#} {-r:0.#} Z"));
            }

            s.Append(Inv($"<path d=\"{d}\" fill=\"{fill}\"/>\n"));
        }

        s.Append("</g>\n");

        // §4.3 — an `edge` room's ground simply stops. The torn boundary is painted back in paper
        // over the land, which is the whole statement: no border, no hatching, nothing past it.
        s.Append("<g id=\"edges\">\n");

        foreach (var pr in Ordered(m).Where(p => p.Role == Role.Edge))
        {
            Tear(s, m, pr);
        }

        s.Append("</g>\n");
    }

    /// <summary>One room's worth of floor, with an edge all the way round and nothing past it.</summary>
    static void Island(StringBuilder s, PlacedRoom pr)
    {
        var rng = new Seeded("island:" + pr.Room.Key);
        var p = pr.Pitch;
        var points = new StringBuilder();
        const int sides = 18;

        for (var i = 0; i < sides; i++)
        {
            var angle = i / (double)sides * Math.PI * 2;
            var radius = p * rng.Between(0.34, 0.56);
            points.Append(Inv($"{pr.X + (Math.Cos(angle) * radius):0.#},{pr.Y + (Math.Sin(angle) * radius * 0.82):0.#} "));
        }

        s.Append(Inv($"<polygon points=\"{points.ToString().Trim()}\" fill=\"{Land}\"/>\n"));

        // Grit that came loose and did not fall, because there is nothing for it to fall to.
        for (var i = 0; i < 14; i++)
        {
            var angle = rng.Unit() * Math.PI * 2;
            var radius = p * rng.Between(0.55, 0.86);

            s.Append(Inv($"<circle cx=\"{pr.X + (Math.Cos(angle) * radius):0.#}\" cy=\"{pr.Y + (Math.Sin(angle) * radius * 0.82):0.#}\" r=\"{rng.Between(1, 2.6):0.##}\" fill=\"{Land}\" opacity=\"{rng.Between(0.25, 0.7):0.##}\"/>\n"));
        }
    }

    static void Tear(StringBuilder s, MapModel m, PlacedRoom pr)
    {
        var (dx, dy) = Outward(m, pr);
        var p = pr.Pitch;
        var rng = new Seeded("tear:" + pr.Room.Key);

        var ax = -dy;
        var ay = dx;
        var cx = pr.X + (dx * p * 0.22);
        var cy = pr.Y + (dy * p * 0.22);
        var half = p * 0.78;

        var pts = new List<(double X, double Y)>();
        const int steps = 11;

        for (var i = 0; i <= steps; i++)
        {
            var t = ((double)i / steps * 2) - 1;
            var jitter = rng.Between(-0.14, 0.14) * p;
            pts.Add((cx + (ax * half * t) + (dx * jitter), cy + (ay * half * t) + (dy * jitter)));
        }

        var far = p * 1.4;
        var path = new StringBuilder(Inv($"M{pts[0].X:0.#} {pts[0].Y:0.#}"));

        foreach (var pt in pts.Skip(1))
        {
            path.Append(Inv($" L{pt.X:0.#} {pt.Y:0.#}"));
        }

        path.Append(Inv($" L{pts[^1].X + (dx * far):0.#} {pts[^1].Y + (dy * far):0.#}"));
        path.Append(Inv($" L{pts[0].X + (dx * far):0.#} {pts[0].Y + (dy * far):0.#} Z"));

        s.Append(Inv($"<path d=\"{path}\" fill=\"{Paper}\"/>\n"));

        // A little of the ground carries on past the tear, and then does not.
        for (var i = 0; i < 34; i++)
        {
            var t = rng.Between(-0.95, 0.95);
            var away = rng.Unit();
            var px = cx + (ax * half * t) + (dx * away * p * 0.6);
            var py = cy + (ay * half * t) + (dy * away * p * 0.6);

            s.Append(Inv($"<circle cx=\"{px:0.#}\" cy=\"{py:0.#}\" r=\"{rng.Between(1.4, 3.6) * (1 - (away * 0.55)):0.##}\" fill=\"{Land}\" opacity=\"{0.95 - (away * 0.85):0.##}\"/>\n"));
        }
    }

    /// <summary>
    /// Which way the world runs out: a cell side with no neighbour and no exit, and of those the
    /// one pointing furthest away from the rest of the zone.
    /// </summary>
    /// <remarks>
    /// Filler counts as ground here even though it is not a room. Without that, The Low Wall finds
    /// its first free side is the block of houses to the east and tears the town open sideways.
    /// </remarks>
    static (int Dx, int Dy) Outward(MapModel m, PlacedRoom pr)
    {
        var zone = m.Zones[pr.Room.ZoneKey];
        var occupied = zone.Rooms.Select(p => (p.GridX, p.GridY)).ToHashSet();

        foreach (var cell in zone.FillerCells)
        {
            occupied.Add(cell);
        }

        var cx = zone.Rooms.Average(r => (double)r.GridX);
        var cy = zone.Rooms.Average(r => (double)r.GridY);
        var best = (Dx: 0, Dy: 1);
        var bestScore = double.NegativeInfinity;

        foreach (var dir in new[] { "north", "south", "east", "west" })
        {
            var (dx, dy) = MapModel.Step(dir);

            if (occupied.Contains((pr.GridX + dx, pr.GridY + dy)) || pr.Room.Exits.Any(e => e.Direction == dir))
            {
                continue;
            }

            var score = ((pr.GridX - cx) * dx) + ((pr.GridY - cy) * dy);

            if (score > bestScore)
            {
                bestScore = score;
                best = (dx, dy);
            }
        }

        return best;
    }

    // -- §4.3 ground texture, read off the room's own terrain grid ------------------------------

    static void Texture(StringBuilder s, MapModel m)
    {
        s.Append("<g id=\"texture\">\n");

        foreach (var pr in Ordered(m))
        {
            if (pr.Role is Role.Building or Role.Road)
            {
                continue;
            }

            var rng = new Seeded("tex:" + pr.Room.Key);
            var grid = pr.Room.Grid;

            if (grid.Count == 0)
            {
                continue;
            }

            var p = pr.Pitch;
            var rows = grid.Count;
            var cols = grid.Max(g => g.Length);

            for (var ry = 0; ry < rows; ry++)
            {
                for (var rx = 0; rx < grid[ry].Length; rx++)
                {
                    var tile = pr.Room.Legend.GetValueOrDefault(grid[ry][rx].ToString());

                    if (tile is null or "void" or "floor" or "path" or "ash")
                    {
                        continue;
                    }

                    // A terrain grid is 21x9 and a map cell is square, so this samples rather than
                    // transcribes: it is texture that recalls the room, not a copy of it. Grass is
                    // most of what open country is made of and gets thinned hard, or the sheet
                    // turns into a lawn.
                    if (rng.Unit() > (tile == "grass" ? 0.09 : 0.4))
                    {
                        continue;
                    }

                    var px = pr.X + ((((rx + 0.5) / cols) - 0.5) * p * 0.98) + rng.Between(-2, 2);
                    var py = pr.Y + ((((ry + 0.5) / rows) - 0.5) * p * 0.98) + rng.Between(-2, 2);

                    switch (tile)
                    {
                        case "tree":
                            Tree(s, px, py, p * rng.Between(0.05, 0.075));
                            break;

                        case "reed":
                            s.Append(Inv($"<path d=\"M{px:0.#} {py:0.#} l0 {-p * 0.05:0.#}\" stroke=\"{Faint}\" stroke-width=\"1.5\"/>\n"));
                            break;

                        case "grass":
                            s.Append(Inv($"<path d=\"M{px - 4:0.#} {py:0.#} q4 -6 4 0 q0 -6 4 0\" fill=\"none\" stroke=\"{Faint}\" stroke-width=\"1.3\" opacity=\"0.45\"/>\n"));
                            break;

                        case "water":
                            s.Append(Inv($"<circle cx=\"{px:0.#}\" cy=\"{py:0.#}\" r=\"{p * 0.06:0.#}\" fill=\"{Water}\" opacity=\"0.9\"/>\n"));
                            break;

                        case "rock":
                            s.Append(Inv($"<path d=\"M{px - 4.5:0.#} {py + 2:0.#} l4.5 -5.5 l5 5.5 z\" fill=\"{Faint}\" opacity=\"0.5\"/>\n"));
                            break;

                        case "rubble":
                            s.Append(Inv($"<circle cx=\"{px:0.#}\" cy=\"{py:0.#}\" r=\"{rng.Between(1, 2.3):0.##}\" fill=\"{Ink}\" opacity=\"0.28\"/>\n"));
                            break;

                        default:
                            s.Append(Inv($"<rect x=\"{px - 2:0.#}\" y=\"{py - 2:0.#}\" width=\"4\" height=\"4\" fill=\"{Ink}\" opacity=\"0.2\"/>\n"));
                            break;
                    }
                }
            }
        }

        s.Append("</g>\n");
    }

    static void Tree(StringBuilder s, double x, double y, double r)
    {
        s.Append(Inv($"<path d=\"M{x:0.#} {y + r:0.#} v{-r * 0.7:0.#}\" stroke=\"{Ink}\" stroke-width=\"1.3\" opacity=\"0.55\"/>"));
        s.Append(Inv($"<circle cx=\"{x:0.#}\" cy=\"{y - (r * 0.2):0.#}\" r=\"{r:0.#}\" fill=\"#8b9a69\" opacity=\"0.6\" stroke=\"{Ink}\" stroke-width=\"0.9\" stroke-opacity=\"0.4\"/>\n"));
    }

    // -- §4.3 roads and tracks -----------------------------------------------------------------

    static void Ways(StringBuilder s, MapModel m)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var roads = new StringBuilder();
        var casing = new StringBuilder();
        var tracks = new StringBuilder();

        foreach (var pr in Ordered(m))
        {
            foreach (var e in pr.Room.Exits)
            {
                if (!MapModel.Lateral.Contains(e.Direction) || !m.Rooms.TryGetValue(e.To, out var other) || !other.Drawn)
                {
                    continue;
                }

                var pair = string.CompareOrdinal(pr.Room.Key, other.Room.Key) < 0
                    ? pr.Room.Key + "|" + other.Room.Key
                    : other.Room.Key + "|" + pr.Room.Key;

                if (!seen.Add(pair))
                {
                    continue;
                }

                var width = Math.Min(pr.Pitch, other.Pitch);

                // Stop the road at a building's wall. Run it to the centre instead and the rounded
                // cap surfaces on the far side of the roof as a blob nobody can name.
                var (fx, fy) = Retract(pr, other, width);
                var (tx, ty) = Retract(other, pr, width);
                var d = Inv($"M{fx:0.#} {fy:0.#} L{tx:0.#} {ty:0.#}");

                if (Metalled(m, pr) && Metalled(m, other))
                {
                    casing.Append(Inv($"<path d=\"{d}\" stroke=\"{Ink}\" stroke-width=\"{width * 0.34:0.#}\" stroke-linecap=\"round\" fill=\"none\" opacity=\"0.8\"/>\n"));
                    roads.Append(Inv($"<path d=\"{d}\" stroke=\"{RoadFill}\" stroke-width=\"{width * 0.27:0.#}\" stroke-linecap=\"round\" fill=\"none\"/>\n"));
                }
                else
                {
                    // §4.3 — open ground with a way through it. Dashed, no band: a track is the
                    // direction of travel, not a built thing.
                    tracks.Append(Inv($"<path d=\"{d}\" stroke=\"{Ink}\" stroke-width=\"{width * 0.023:0.#}\" stroke-linecap=\"round\" fill=\"none\" opacity=\"0.5\" stroke-dasharray=\"{width * 0.075:0.#} {width * 0.05:0.#}\"/>\n"));
                }
            }
        }

        s.Append("<g id=\"tracks\">\n").Append(tracks).Append("</g>\n");
        s.Append("<g id=\"roads\">\n").Append(casing).Append(roads).Append("</g>\n");

        // Squares are drawn over the streets that meet in them.
        s.Append("<g id=\"plazas\">\n");

        foreach (var pr in Ordered(m).Where(p => p.Role == Role.Plaza))
        {
            var p = pr.Pitch;
            var half = p * 0.44;
            var grassy = pr.Kind is "thicket" or "scrub" or "gateroad" or "pool" or "outcrop";
            var fill = grassy ? Green : RoadFill;

            s.Append(Inv($"<rect x=\"{pr.X - half:0.#}\" y=\"{pr.Y - half:0.#}\" width=\"{half * 2:0.#}\" height=\"{half * 2:0.#}\" rx=\"{p * (grassy ? 0.3 : 0.05):0.#}\" fill=\"{fill}\" stroke=\"{Ink}\" stroke-width=\"2.2\" stroke-opacity=\"0.7\"/>\n"));

            if (!grassy)
            {
                s.Append(Inv($"<rect x=\"{pr.X - half:0.#}\" y=\"{pr.Y - half:0.#}\" width=\"{half * 2:0.#}\" height=\"{half * 2:0.#}\" rx=\"{p * 0.05:0.#}\" fill=\"url(#paved)\"/>\n"));
            }
        }

        s.Append("</g>\n");
    }

    /// <summary>
    /// A street is a built thing, and only a town builds them. Open ground and the rim never carry
    /// one however the rooms either side are classified — otherwise Gatetown gets a paved road
    /// along the riverbank and out over the edge of the world.
    /// </summary>
    /// <summary>Where a way stops: a building's wall, or the room's centre for anything else.</summary>
    static (double X, double Y) Retract(PlacedRoom from, PlacedRoom towards, double width)
    {
        if (from.Role is not (Role.Building or Role.Ruin))
        {
            return (from.X, from.Y);
        }

        // Half the stroke on top of the wall distance: a round cap reaches back past the point it
        // is drawn to, so stopping exactly at the wall puts a bulge inside the building.
        var p = Math.Min(from.Pitch, 145);
        var dx = towards.X - from.X;
        var dy = towards.Y - from.Y;

        return Math.Abs(dx) >= Math.Abs(dy)
            ? (from.X + (Math.Sign(dx) * ((p * 0.31) + (width * 0.17))), from.Y)
            : (from.X, from.Y + (Math.Sign(dy) * ((p * 0.23) + (width * 0.17))));
    }

    static bool Metalled(MapModel m, PlacedRoom p) =>
        m.Zones[p.Room.ZoneKey].Density == "town" && p.Role is not (Role.Open or Role.Edge);

    // -- §4.4 filler ---------------------------------------------------------------------------

    static void Filler(StringBuilder s, MapModel m)
    {
        s.Append("<g id=\"filler\">\n");

        foreach (var zl in m.Zones.Values.Where(z => z.FillerCells.Count > 0)
            .OrderBy(z => z.Zone.Key, StringComparer.Ordinal))
        {
            foreach (var (gx, gy) in zl.FillerCells)
            {
                var rng = new Seeded(Inv($"fill:{zl.Zone.Key}:{gx}:{gy}"));
                var cx = zl.OriginX + ((gx - zl.MinX) * zl.Pitch);
                var cy = zl.OriginY + ((gy - zl.MinY) * zl.Pitch);
                var p = zl.Pitch;
                var count = rng.Int(3, 6);

                for (var i = 0; i < count; i++)
                {
                    var bw = p * rng.Between(0.24, 0.36);
                    var bh = p * rng.Between(0.2, 0.3);
                    var bx = cx + (p * rng.Between(-0.34, 0.34)) - (bw / 2);
                    var by = cy + (p * rng.Between(-0.32, 0.32)) - (bh / 2);

                    s.Append(Inv($"<rect x=\"{bx:0.#}\" y=\"{by:0.#}\" width=\"{bw:0.#}\" height=\"{bh:0.#}\" fill=\"{Paper}\" stroke=\"{Ink}\" stroke-width=\"1.7\" stroke-opacity=\"0.5\"/>\n"));
                    s.Append(Inv($"<rect x=\"{bx:0.#}\" y=\"{by:0.#}\" width=\"{bw:0.#}\" height=\"{bh:0.#}\" fill=\"url(#hatchfill)\"/>\n"));
                }
            }
        }

        s.Append("</g>\n");
    }

    // -- §4.3 buildings ------------------------------------------------------------------------

    static void Buildings(StringBuilder s, MapModel m)
    {
        s.Append("<g id=\"buildings\">\n");

        foreach (var pr in Ordered(m).Where(p => p.Role is Role.Building or Role.Ruin))
        {
            // A building is a building. Scaled with the zone pitch, a lone shed out in the
            // Cutting draws half again the size of Grask's ironmonger, because the wilderness
            // around it is drawn at a coarser scale than the town — which says the shed is bigger,
            // and it is not. Pitch sets where a building sits; it does not set how big it is.
            var p = Math.Min(pr.Pitch, 145);
            var hint = m.Sidecar.Rooms.GetValueOrDefault(pr.Room.Key);
            var fw = (hint?.Footprint?[0] ?? 1) * p * 0.62;
            var fh = (hint?.Footprint?[1] ?? 1) * p * 0.46;
            var x = pr.X - (fw / 2);
            var y = pr.Y - (fh / 2);

            if (pr.Role == Role.Ruin)
            {
                s.Append(Inv($"<path d=\"M{x + (fw * 0.6):0.#} {y:0.#} H{x:0.#} V{y + fh:0.#} H{x + fw:0.#} V{y + (fh * 0.4):0.#}\" fill=\"{Paper}\" fill-opacity=\"0.65\" stroke=\"{Ink}\" stroke-width=\"2.4\" stroke-linejoin=\"round\"/>\n"));

                // Three standing walls read as an unfinished drawing until something has fallen
                // inside them.
                var rubble = new Seeded("ruin:" + pr.Room.Key);

                for (var k = 0; k < 16; k++)
                {
                    s.Append(Inv($"<circle cx=\"{x + (fw * rubble.Between(0.12, 0.88)):0.#}\" cy=\"{y + (fh * rubble.Between(0.15, 0.85)):0.#}\" r=\"{rubble.Between(1.1, 2.8):0.##}\" fill=\"{Ink}\" opacity=\"{rubble.Between(0.2, 0.45):0.##}\"/>\n"));
                }

                continue;
            }

            s.Append(Inv($"<rect x=\"{x:0.#}\" y=\"{y:0.#}\" width=\"{fw:0.#}\" height=\"{fh:0.#}\" fill=\"{Paper}\" stroke=\"{Ink}\" stroke-width=\"2.8\"/>\n"));
            s.Append(Inv($"<rect x=\"{x:0.#}\" y=\"{y:0.#}\" width=\"{fw:0.#}\" height=\"{fh:0.#}\" fill=\"url(#hatch)\"/>\n"));

            // The door goes in the wall facing the room this one opens onto (§3.2 frontage).
            if (pr.Room.Exits.FirstOrDefault(e => MapModel.Lateral.Contains(e.Direction)) is { } frontage)
            {
                var (dx, dy) = MapModel.Step(frontage.Direction);
                var door = Math.Min(fw, fh) * 0.34;
                var dxp = pr.X + (dx * fw / 2);
                var dyp = pr.Y + (dy * fh / 2);

                s.Append(Inv($"<rect x=\"{dxp - (dx == 0 ? door / 2 : 3.5):0.#}\" y=\"{dyp - (dy == 0 ? door / 2 : 3.5):0.#}\" width=\"{(dx == 0 ? door : 7):0.#}\" height=\"{(dy == 0 ? door : 7):0.#}\" fill=\"{RoadFill}\" stroke=\"{Ink}\" stroke-width=\"1.5\"/>\n"));
            }

            if (pr.Shop is not null)
            {
                Sign(s, pr.X + (fw * 0.5) + (p * 0.06), pr.Y - (fh * 0.36), ShopGlyph(pr.Shop), p / 130);
            }
        }

        s.Append("</g>\n");
    }

    static string ShopGlyph(Shop shop)
    {
        var sells = string.Join(' ', shop.Sells);

        return sells.Contains("blade", StringComparison.Ordinal) || sells.Contains("axe", StringComparison.Ordinal)
            || sells.Contains("staff", StringComparison.Ordinal) || sells.Contains("knuckle", StringComparison.Ordinal) ? "blade"
            : sells.Contains("leather", StringComparison.Ordinal) || sells.Contains("shield", StringComparison.Ordinal) ? "shield"
            : sells.Contains("bread", StringComparison.Ordinal) ? "loaf"
            : "pack";
    }

    static void Sign(StringBuilder s, double x, double y, string glyph, double scale)
    {
        s.Append(Inv($"<g transform=\"translate({x:0.#} {y:0.#}) scale({scale:0.###})\" stroke=\"{Ink}\" stroke-width=\"1.9\" fill=\"none\" stroke-linecap=\"round\" stroke-linejoin=\"round\">"));
        s.Append(Inv($"<circle cx=\"0\" cy=\"0\" r=\"12\" fill=\"{Paper}\" stroke-width=\"1.6\"/>"));

        s.Append(glyph switch
        {
            "blade" => Inv($"<path d=\"M-5 4 L3 -5 L5 -3 L-3 6 Z\" fill=\"{Paper}\"/><path d=\"M-6 5 l2.5 2.5\"/>"),
            "shield" => Inv($"<path d=\"M0 -6 L6 -3.5 V0 Q6 5 0 6.5 Q-6 5 -6 0 V-3.5 Z\" fill=\"none\"/>"),
            "loaf" => Inv($"<path d=\"M-6 3 Q-6 -4 0 -4 Q6 -4 6 3 Z\" fill=\"none\"/><path d=\"M-2 -3.5 l-1 6 M2 -3.5 l1 6\"/>"),
            _ => Inv($"<path d=\"M-5 -2 h10 v7 h-10 Z\" fill=\"none\"/><path d=\"M-2.5 -2 v-2.5 h5 v2.5\"/>"),
        });

        s.Append("</g>\n");
    }

    // -- §3.2 road-run names, lettered along the road ------------------------------------------

    static HashSet<string> WayLabels(StringBuilder s, MapModel m, Labels labels)
    {
        var lettered = new HashSet<string>(StringComparer.Ordinal);

        s.Append("<g id=\"waylabels\">\n");
        var i = 0;

        foreach (var run in m.Runs)
        {
            i++;

            if (run.Label is null)
            {
                continue;
            }

            var rooms = run.Rooms;
            var text = run.Label.ToUpperInvariant();
            var pitch = rooms.Min(r => r.Pitch);
            var size = pitch * 0.1;
            var tracking = size * 0.24;

            var length = 0.0;

            for (var k = 1; k < rooms.Count; k++)
            {
                length += Math.Abs(rooms[k].X - rooms[k - 1].X) + Math.Abs(rooms[k].Y - rooms[k - 1].Y);
            }

            // A textPath that overruns its path is silently clipped, which is how "THE HIGH SHELF"
            // becomes "HE HIGH SHEL". Shrink to fit, and give up rather than clip.
            while (Labels.Width(text, size, tracking) > length * 0.9 && size > pitch * 0.055)
            {
                size *= 0.92;
                tracking = size * 0.24;
            }

            if (Labels.Width(text, size, tracking) > length * 0.9)
            {
                continue;
            }

            var d = new StringBuilder(Inv($"M{rooms[0].X:0.#} {rooms[0].Y:0.#}"));

            foreach (var r in rooms.Skip(1))
            {
                d.Append(Inv($" L{r.X:0.#} {r.Y:0.#}"));
            }

            // Centre the name on the longest stretch of road that is not a square. Left at the
            // plain midpoint, both of Gatetown's streets letter themselves straight down The Gate
            // Yard, because the square is exactly where the town's roads cross.
            var (offset, mid) = ClearestStretch(rooms, length, Labels.Width(text, size, tracking));

            if (offset < 0)
            {
                continue;
            }

            var id = "run" + i.ToString(CultureInfo.InvariantCulture);

            s.Append(Inv($"<path id=\"{id}\" d=\"{d}\" fill=\"none\"/>\n"));
            s.Append(Inv($"<text font-family=\"{Serif}\" font-size=\"{size:0.#}\" letter-spacing=\"{tracking:0.#}\" fill=\"{Ink}\" stroke=\"{Paper}\" stroke-width=\"{size * 0.34:0.#}\" paint-order=\"stroke\" stroke-linejoin=\"round\" dy=\"{size * 0.34:0.#}\">"));
            s.Append(Inv($"<textPath xlink:href=\"#{id}\" startOffset=\"{offset * 100:0.##}%\" text-anchor=\"middle\">{Esc(text)}</textPath></text>\n"));

            // The claim has to follow the text, which a vertical run rotates ninety degrees. Claimed
            // flat, "NORTH LANE" running down the road reserves nothing and the square's own name
            // is set straight through it.
            var extent = Labels.Width(text, size, tracking);

            if (run.Horizontal)
            {
                labels.Claim(mid.X, mid.Y + (size * 0.65), extent, size * 1.3);
            }
            else
            {
                labels.Claim(mid.X, mid.Y + (extent / 2), size * 1.3, extent);
            }

            foreach (var r in rooms)
            {
                lettered.Add(r.Room.Key);
            }
        }

        s.Append("</g>\n");
        return lettered;
    }

    /// <summary>
    /// Walks the run and finds where along it the name should sit: the middle of the longest span
    /// that no square occupies. Returns the fraction along the path, and the point there — or -1
    /// when no span is long enough to hold the name without running over a square.
    /// </summary>
    static (double Offset, (double X, double Y) At) ClearestStretch(List<PlacedRoom> rooms, double length, double needed)
    {
        if (length <= 0)
        {
            return (-1, (0, 0));
        }

        const int samples = 200;
        var blocked = new bool[samples + 1];

        for (var i = 0; i <= samples; i++)
        {
            var at = PointAlong(rooms, (double)i / samples * length);

            blocked[i] = rooms.Any(r => r.Role == Role.Plaza
                && Math.Abs(r.X - at.X) < r.Pitch * 0.5
                && Math.Abs(r.Y - at.Y) < r.Pitch * 0.5);
        }

        var bestStart = -1;
        var bestLen = 0;
        var start = -1;

        for (var i = 0; i <= samples; i++)
        {
            if (!blocked[i])
            {
                start = start < 0 ? i : start;

                if (i - start + 1 > bestLen)
                {
                    bestLen = i - start + 1;
                    bestStart = start;
                }
            }
            else
            {
                start = -1;
            }
        }

        if (bestStart < 0 || (double)bestLen / samples * length < needed)
        {
            return (-1, (0, 0));
        }

        // Keep the whole name on the path. A textPath that starts too near either end is clipped
        // without complaint, which is how "THE GATE ROAD" reaches the sheet as "HE GATE ROAD".
        var fraction = (bestStart + (bestLen / 2.0)) / samples;
        var margin = needed / 2 / length;
        fraction = Math.Clamp(fraction, margin, 1 - margin);

        return (fraction, PointAlong(rooms, fraction * length));
    }

    static (double X, double Y) PointAlong(List<PlacedRoom> rooms, double distance)
    {
        for (var i = 1; i < rooms.Count; i++)
        {
            var dx = rooms[i].X - rooms[i - 1].X;
            var dy = rooms[i].Y - rooms[i - 1].Y;
            var segment = Math.Sqrt((dx * dx) + (dy * dy));

            if (distance <= segment || i == rooms.Count - 1)
            {
                var t = segment <= 0 ? 0 : Math.Clamp(distance / segment, 0, 1);
                return (rooms[i - 1].X + (dx * t), rooms[i - 1].Y + (dy * t));
            }

            distance -= segment;
        }

        return (rooms[0].X, rooms[0].Y);
    }

    // -- everything else that carries a name ----------------------------------------------------

    static void PlaceLabels(StringBuilder s, MapModel m, Labels labels, HashSet<string> lettered)
    {
        s.Append("<g id=\"placelabels\">\n");

        var named = m.Runs.Where(r => r.Label is not null).SelectMany(r => r.Rooms)
            .Select(r => r.Room.Key).ToHashSet(StringComparer.Ordinal);

        // Buildings and squares first — they are the things a reader looks for by name.
        foreach (var pr in Ordered(m).Where(p => p.Role is Role.Building or Role.Ruin or Role.Plaza)
            .OrderBy(p => p.Pitch))
        {
            var plaza = pr.Role == Role.Plaza;
            var p = plaza ? pr.Pitch : Math.Min(pr.Pitch, 145);
            Anchored(s, labels, pr.X, pr.Y, p, pr.Label, p * (plaza ? 0.1 : 0.095), plaza, italic: false, from: plaza ? p * 0.0 : p * 0.34);
        }

        foreach (var pr in Ordered(m).Where(p => p.Role is Role.Open or Role.Edge or Role.Road or Role.Track))
        {
            // Only a room whose run actually got its name lettered along it is left unlabelled —
            // a run the renderer had to skip must not take its rooms' names down with it.
            if (lettered.Contains(pr.Room.Key) && pr.Role is Role.Road or Role.Track)
            {
                continue;
            }

            var p = pr.Pitch;
            Anchored(s, labels, pr.X, pr.Y, p, pr.Label, p * 0.082, bold: false, italic: true, from: p * 0.3);
        }

        s.Append("</g>\n");
    }

    /// <summary>Places a name near its subject, stepping away until it stops hitting another.</summary>
    static void Anchored(StringBuilder s, Labels labels, double x, double y, double pitch, string text, double size, bool bold, bool italic, double from)
    {
        var w = Labels.Width(text, size);
        var h = size * 1.25;

        double[][] offsets =
        [
            [0, from], [0, -from + (size * 0.2)], [0, from + (size * 1.3)], [0, -from - (size * 1.1)],
            [(w / 2) + (pitch * 0.2), size * 0.3], [-(w / 2) - (pitch * 0.2), size * 0.3],
            [0, from + (size * 2.6)],
        ];

        foreach (var o in offsets)
        {
            if (!labels.Free(x + o[0], y + o[1] + (size * 0.35), w, h))
            {
                continue;
            }

            Text(s, x + o[0], y + o[1] + (size * 0.35), text, size, bold, italic, 0);
            labels.Claim(x + o[0], y + o[1] + (size * 0.35), w, h);
            return;
        }

        // Nowhere clear: draw it anyway at the first choice. A name fighting for room is a better
        // fault than a place with no name on it.
        Text(s, x, y + from + (size * 0.35), text, size, bold, italic, 0);
        labels.Claim(x, y + from + (size * 0.35), w, h);
    }

    static void MarkerLabels(StringBuilder s, MapModel m, Labels labels)
    {
        s.Append("<g id=\"markers\">\n");

        foreach (var mk in m.Markers)
        {
            var p = mk.Host.Pitch;
            var x = mk.Host.X - (p * 0.34);
            var y = mk.Host.Y - (p * 0.26);

            s.Append(Inv($"<g transform=\"translate({x:0.#} {y:0.#}) scale({p / 150:0.###})\">"));
            s.Append(Glyph(mk.Glyph));
            s.Append("</g>\n");

            Anchored(s, labels, x, y, p, mk.Label, p * 0.078, bold: false, italic: true, from: p * 0.13);
        }

        s.Append("</g>\n");
    }

    static string Glyph(string glyph) => glyph switch
    {
        // A stair going down into the dark: the Midgaard sheet's answer for "Sewer Entrance".
        "cellar" => Inv($"<rect x=\"-15\" y=\"-15\" width=\"30\" height=\"30\" rx=\"3\" fill=\"{Paper}\" stroke=\"{Ink}\" stroke-width=\"2.6\"/>")
            + Inv($"<path d=\"M-9 -9 h6 v6 h6 v6 h6\" fill=\"none\" stroke=\"{Ink}\" stroke-width=\"2.2\" stroke-linejoin=\"round\"/>"),
        "cave" => Inv($"<path d=\"M-15 14 V0 A15 15 0 0 1 15 0 V14 Z\" fill=\"{Ink}\" fill-opacity=\"0.8\" stroke=\"{Ink}\" stroke-width=\"2.4\" stroke-linejoin=\"round\"/>"),
        "portal" => Inv($"<circle cx=\"0\" cy=\"0\" r=\"14\" fill=\"{Paper}\" stroke=\"{Ink}\" stroke-width=\"3.4\"/>")
            + Inv($"<circle cx=\"0\" cy=\"0\" r=\"7\" fill=\"{Ink}\" fill-opacity=\"0.28\"/>"),
        _ => Inv($"<rect x=\"-15\" y=\"-15\" width=\"30\" height=\"30\" rx=\"3\" fill=\"{Paper}\" stroke=\"{Ink}\" stroke-width=\"2.6\"/>")
            + Inv($"<path d=\"M-9 9 h6 v-6 h6 v-6 h6\" fill=\"none\" stroke=\"{Ink}\" stroke-width=\"2.2\" stroke-linejoin=\"round\"/>"),
    };

    // -- §4.6.3 border crossings ---------------------------------------------------------------

    static void BorderLabels(StringBuilder s, MapModel m, Labels labels)
    {
        s.Append("<g id=\"borders\">\n");

        foreach (var b in m.Borders)
        {
            var pr = b.From;
            var p = pr.Pitch;
            var (dx, dy) = Outward(m, pr);
            var x = pr.X + (dx * p * 0.8);
            var y = pr.Y + (dy * p * 0.8);

            s.Append(Inv($"<g transform=\"translate({x:0.#} {y:0.#}) scale({p / 120:0.###})\">"));
            s.Append(Glyph("portal"));
            s.Append("</g>\n");

            Text(s, x, y + (p * 0.22), b.Caption + " →", p * 0.1, bold: true, italic: false, tracking: p * 0.012);
        }

        s.Append("</g>\n");
    }

    // -- §4.5 the sheet ------------------------------------------------------------------------

    static void Sheet(StringBuilder s, MapModel m, Labels labels, double x0, double y0, double w, double h)
    {
        s.Append("<g id=\"sheet\">\n");

        const double inset = 40;

        // Zone names go in the margin, running up the sheet beside the block they name. Set across
        // the top of the block instead they land on whatever the zone above ends with — and in a
        // realm shaped like Ossara, every zone has one directly above it.
        foreach (var zl in m.Zones.Values.Where(z => z.Rooms.Count > 0).OrderBy(z => z.Zone.Key, StringComparer.Ordinal))
        {
            var cy = (zl.Rooms.Min(r => r.Y) + zl.Rooms.Max(r => r.Y)) / 2;
            var cx = x0 + inset + 52;
            const double size = 30;
            var text = zl.Zone.Name.ToUpperInvariant();
            var extent = Labels.Width(text, size, size * 0.44);

            s.Append(Inv($"<text x=\"{cx:0.#}\" y=\"{cy:0.#}\" transform=\"rotate(-90 {cx:0.#} {cy:0.#})\" text-anchor=\"middle\" font-family=\"{Serif}\" font-size=\"{size:0.#}\" letter-spacing=\"{size * 0.44:0.#}\" fill=\"{Faint}\">{Esc(text)}</text>\n"));
            labels.Claim(cx, cy + (extent / 2), size * 1.6, extent);
        }

        s.Append(Inv($"<rect x=\"{x0 + inset:0.#}\" y=\"{y0 + inset:0.#}\" width=\"{w - (inset * 2):0.#}\" height=\"{h - (inset * 2):0.#}\" fill=\"none\" stroke=\"{Ink}\" stroke-width=\"5\"/>\n"));
        s.Append(Inv($"<rect x=\"{x0 + inset + 11:0.#}\" y=\"{y0 + inset + 11:0.#}\" width=\"{w - (inset * 2) - 22:0.#}\" height=\"{h - (inset * 2) - 22:0.#}\" fill=\"none\" stroke=\"{Ink}\" stroke-width=\"1.7\"/>\n"));

        // Title.
        var tx = x0 + w - inset - 46;
        var ty = y0 + inset + 108;
        var title = m.Sidecar.Title ?? m.World.Name;

        s.Append(Inv($"<text x=\"{tx:0.#}\" y=\"{ty:0.#}\" text-anchor=\"end\" font-family=\"{Serif}\" font-size=\"84\" letter-spacing=\"7\" fill=\"{Ink}\">{Esc(title)}</text>\n"));
        labels.Claim(tx - (Labels.Width(title, 84, 7) / 2), ty + 14, Labels.Width(title, 84, 7), 110);

        if (m.Sidecar.Subtitle is { } subtitle)
        {
            s.Append(Inv($"<text x=\"{tx:0.#}\" y=\"{ty + 44:0.#}\" text-anchor=\"end\" font-family=\"{Serif}\" font-style=\"italic\" font-size=\"29\" fill=\"{Faint}\">{Esc(subtitle)}</text>\n"));
        }

        // North arrow.
        var nx = x0 + inset + 96;
        var ny = y0 + inset + 116;
        s.Append(Inv($"<g transform=\"translate({nx:0.#} {ny:0.#})\">"));
        s.Append(Inv($"<path d=\"M0 -56 L17 22 L0 7 L-17 22 Z\" fill=\"{Ink}\"/>"));
        s.Append(Inv($"<text x=\"0\" y=\"60\" text-anchor=\"middle\" font-family=\"{Serif}\" font-size=\"36\" letter-spacing=\"3\" fill=\"{Ink}\">N</text></g>\n"));
        labels.Claim(nx, ny + 66, 120, 140);

        s.Append("</g>\n");
    }

    // -- helpers -------------------------------------------------------------------------------

    static IEnumerable<PlacedRoom> Ordered(MapModel m) =>
        m.Rooms.Values.Where(p => p.Drawn).OrderBy(p => p.Room.Key, StringComparer.Ordinal);

    static void Text(StringBuilder s, double x, double y, string text, double size, bool bold, bool italic, double tracking)
    {
        var weight = bold ? " font-weight=\"600\"" : "";
        var style = italic ? " font-style=\"italic\"" : "";
        var track = tracking > 0 ? Inv($" letter-spacing=\"{tracking:0.#}\"") : "";

        s.Append(Inv($"<text x=\"{x:0.#}\" y=\"{y:0.#}\" text-anchor=\"middle\" font-family=\"{Serif}\" font-size=\"{size:0.#}\"{weight}{style}{track} fill=\"{Ink}\" stroke=\"{Paper}\" stroke-width=\"{size * 0.38:0.#}\" paint-order=\"stroke\" stroke-linejoin=\"round\">{Esc(text)}</text>\n"));
    }

    static string Esc(string s) => s
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal);

    /// <summary>Every number in the file is formatted invariantly, or the map stops being stable.</summary>
    static string Inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}
