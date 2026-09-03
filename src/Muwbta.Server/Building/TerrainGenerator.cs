using Muwbta.Engine.Presentation;

namespace Muwbta.Server.Building;

/// <summary>One room's map: the rows, and what each glyph in them means.</summary>
public sealed record RoomTerrain(IReadOnlyList<string> Grid, IReadOnlyDictionary<string, string> Legend);

/// <summary>
/// A named terrain, its tiles, and how they are arranged.
/// </summary>
/// <param name="Key">What a builder picks. Lowercase, stable — it is stored nowhere but is typed.</param>
/// <param name="Summary">One line, shown beside the key in the builder.</param>
/// <param name="Shape">Which of the four arrangements below draws it.</param>
/// <param name="Base">The tile everything starts as. Always placeable, so a room is never sealed.</param>
/// <param name="Feature">The wall, the track, the blob, or the void — see <see cref="TerrainShape"/>.</param>
/// <param name="Furniture">
/// Placed on a loose grid: pillars, tables, crates, braziers. Things somebody put there, which is
/// why they are not scattered — pillars at random do not read as a building.
/// </param>
/// <param name="Debris">
/// Scattered: rubble, rock, trees. Things nobody placed, which is exactly why they are random.
/// </param>
public sealed record TerrainKind(
    string Key,
    string Summary,
    TerrainShape Shape,
    string Base,
    string Feature,
    string? Furniture = null,
    string? Debris = null);

/// <summary>How a kind is drawn.</summary>
public enum TerrainShape
{
    /// <summary>A box border with an interior: halls, tents, sheds.</summary>
    Enclosed,

    /// <summary>Open ground with a track worn across it.</summary>
    Open,

    /// <summary>Open ground with one organic mass in it — a pool, a thicket.</summary>
    Blob,

    /// <summary>Ground that stops. The rim, and the Unlit.</summary>
    Dissolving,
}

/// <summary>
/// Draws a room's terrain from its key.
/// </summary>
/// <remarks>
/// <para>
/// <b>A rebuild, not a port.</b> The original was a Python script that drew all 224 rooms of the
/// Reaches and was never committed — <c>603cb6f</c> carries its output and its validator and not
/// the thing itself. What survives is enough to reconstruct it: the grids in <c>content/</c> fix
/// the geometry (21×9, always), the glyphs, and twenty-one distinct tile-sets that match the
/// twenty-one kinds WORLD.md §10.1 describes.
/// </para>
/// <para>
/// <b>Code rather than a model, deliberately.</b> An assist draft costs minutes and cannot be
/// trusted with a grid: JSON Schema cannot express "rows all the same width", "every glyph is in
/// the legend" or "at least forty cells to stand on", so a generated map would have to be checked
/// and usually redrawn. Choosing a terrain kind is a dropdown. Drawing one is arithmetic. Neither
/// wants inference.
/// </para>
/// <para>
/// <b>Seeded from the room key, with a hash written out here.</b> WORLD.md is explicit that this
/// is what makes regeneration byte-identical, and that a random seed would rewrite every room on
/// every run and leave no diff anybody could read. Two things would quietly break that promise:
/// <c>string.GetHashCode</c> is randomised per process, and <c>System.Random</c>'s sequence is not
/// contracted to be stable between .NET versions. So both the hash and the generator are spelled
/// out below — nine lines to own, against a guarantee that is the whole point.
/// </para>
/// </remarks>
public static class TerrainGenerator
{
    /// <summary>The layout service's own default size, so a room with terrain and one without match.</summary>
    public const int Width = 21;

    /// <summary>As <see cref="Width"/>.</summary>
    public const int Height = 9;

    /// <summary>
    /// What <c>BundleValidator</c> demands, restated so this can refuse to produce it.
    /// </summary>
    /// <remarks>
    /// Entities are drawn only on open ground and are simply not drawn when there is none, so a
    /// room that fails this is a room whose occupants vanish. Every kind is swept against it.
    /// </remarks>
    public const int MinOpenCells = 40;

    /// <summary>The glyph each tile is drawn with.</summary>
    /// <remarks>
    /// Taken from <c>content/</c> rather than invented, so a regenerated room looks like the ones
    /// beside it. Every one is a single BMP character: <c>RoomLayoutService</c> indexes terrain
    /// rows per <c>char</c>, so a surrogate pair — any emoji — would be split across two cells and
    /// neither half would draw.
    /// </remarks>
    private static readonly Dictionary<string, char> Glyphs = new(StringComparer.Ordinal)
    {
        ["floor"] = '.',
        ["path"] = '·',
        ["grass"] = '"',
        ["reed"] = '"',
        ["rubble"] = '░',
        ["rock"] = '▓',
        ["ash"] = '~',
        ["void"] = ' ',
        ["water"] = '≈',
        ["tree"] = '♣',
        ["pillar"] = '▮',
        ["table"] = '▬',
        ["crate"] = '▣',
        ["brazier"] = '♦',
    };

    /// <summary>
    /// The twenty-one kinds, reconstructed from the tile-sets in <c>content/</c>.
    /// </summary>
    /// <remarks>
    /// The tile-sets are exact — they are what the shipped rooms use, counted. The <em>names</em>
    /// are partly reconstruction: WORLD.md §10.1 names ten of the twenty-one outright and the rest
    /// are read off what the tiles obviously are. Two are load-bearing rather than decorative and
    /// keep the names the document gives them: <c>rim</c>, where the land stops, and
    /// <c>standing</c>, which is the Unlit — a floor with void all round it and nothing underneath.
    /// </remarks>
    public static IReadOnlyList<TerrainKind> Kinds { get; } =
    [
        // Enclosed - a border, and what is inside it.
        new("hall", "A pillared interior.", TerrainShape.Enclosed, "floor", "wall", "pillar"),
        new("taproom", "Tables under a roof.", TerrainShape.Enclosed, "floor", "wall", "table"),
        new("store", "Four walls and stacked crates.", TerrainShape.Enclosed, "floor", "wall", "crate"),
        new("shrine", "An interior lit by braziers.", TerrainShape.Enclosed, "floor", "wall", "brazier"),
        new("ruin", "Walls still standing over rubble.", TerrainShape.Enclosed, "floor", "wall", null, "rubble"),
        new("collapse", "A ruin with the roof in it.", TerrainShape.Enclosed, "floor", "wall", null, "rubble rock"),
        new("street", "A way between buildings, crates against the walls.", TerrainShape.Enclosed, "path", "wall", "crate"),
        new("market", "A way between buildings, stalls along it.", TerrainShape.Enclosed, "path", "wall", "table"),

        // Open - a track worn across ground, and what is loose on it.
        new("waste", "Broken ground with a track worn across it.", TerrainShape.Open, "rubble", "path", null, "rock"),
        new("gateroad", "Open grass with a road through it.", TerrainShape.Open, "grass", "path", null, "tree"),
        new("scree", "Loose stone with rock breaking through.", TerrainShape.Open, "floor", "rock", null, "rock"),
        new("ashfield", "Ash over rock.", TerrainShape.Open, "ash", "rock", null, "rock"),

        // Blob - one organic mass in open ground.
        new("marsh", "Reed beds and standing water.", TerrainShape.Blob, "reed", "water"),
        new("pool", "Grass around open water.", TerrainShape.Blob, "grass", "water"),
        new("sink", "Rubble around standing water.", TerrainShape.Blob, "rubble", "water"),
        new("thicket", "Grass closing into trees.", TerrainShape.Blob, "grass", "tree"),
        new("outcrop", "Grass with stone pushing through.", TerrainShape.Blob, "grass", "rock"),
        new("scrub", "Grass, rubble and scattered trees.", TerrainShape.Blob, "grass", "tree", null, "rubble"),

        // Dissolving - ground that stops.
        new("rim", "Where the land stops.", TerrainShape.Dissolving, "rubble", "void"),
        new("brink", "Grass to the edge, and nothing past it.", TerrainShape.Dissolving, "grass", "void"),
        new("standing", "A floor with nothing underneath it.", TerrainShape.Dissolving, "floor", "void"),
    ];

    /// <summary>The kind with this key, or null.</summary>
    public static TerrainKind? Find(string? key) =>
        key is null ? null : Kinds.FirstOrDefault(k => k.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Draws one room.
    /// </summary>
    /// <param name="kindKey">One of <see cref="Kinds"/>.</param>
    /// <param name="roomKey">
    /// The seed. The same room key always draws the same map, which is what lets a zone be
    /// regenerated without rewriting every room in the diff.
    /// </param>
    public static RoomTerrain Generate(string kindKey, string roomKey)
    {
        var kind = Find(kindKey)
            ?? throw new ArgumentException($"There is no terrain kind '{kindKey}'.", nameof(kindKey));

        var rng = new Rng(Hash(roomKey ?? string.Empty));
        var cells = new string[Height][];

        for (var y = 0; y < Height; y++)
        {
            cells[y] = new string[Width];
            Array.Fill(cells[y], kind.Base);
        }

        switch (kind.Shape)
        {
            case TerrainShape.Enclosed:
                Enclose(cells, kind, rng);
                break;
            case TerrainShape.Open:
                OpenGround(cells, kind, rng);
                break;
            case TerrainShape.Blob:
                Blob(cells, kind, rng);
                break;
            default:
                Dissolve(cells, kind, rng);
                break;
        }

        return Render(cells);
    }

    /// <summary>A border, and something regular inside it.</summary>
    /// <remarks>
    /// The border is drawn from box-drawing corners and runs, all legended as <c>wall</c> — five
    /// glyphs meaning one tile, which is how the shipped rooms do it and why the legend is a map
    /// rather than a list.
    /// </remarks>
    private static void Enclose(string[][] cells, TerrainKind kind, Rng rng)
    {
        for (var x = 0; x < Width; x++)
        {
            cells[0][x] = kind.Feature;
            cells[Height - 1][x] = kind.Feature;
        }

        for (var y = 0; y < Height; y++)
        {
            cells[y][0] = kind.Feature;
            cells[y][Width - 1] = kind.Feature;
        }

        Furnish(cells, kind, rng, 2, Width - 2, 2, Height - 2);
    }

    /// <summary>
    /// Puts a kind's furniture and its debris into a region.
    /// </summary>
    /// <remarks>
    /// The two are placed differently on purpose. Furniture sits on a loose grid because somebody
    /// put it there and a hall's pillars hold a roof up; debris is scattered because nobody did.
    /// Both budgets are capped, and the cap is what guarantees <see cref="MinOpenCells"/> by
    /// construction rather than by checking afterwards and hoping.
    /// </remarks>
    private static void Furnish(
        string[][] cells, TerrainKind kind, Rng rng, int x0, int x1, int y0, int y1)
    {
        if (kind.Furniture is { } furniture)
        {
            var stepX = 4 + rng.Next(2);
            var stepY = 3 + rng.Next(2);

            for (var y = y0; y < y1; y += stepY)
            {
                for (var x = x0 + rng.Next(2); x < x1; x += stepX)
                {
                    cells[y][x] = furniture;
                }
            }
        }

        if (kind.Debris is not { } debris)
        {
            return;
        }

        // A kind may scatter two things - "rubble rock" - which is how the shipped collapses read.
        var kinds = debris.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < 14; i++)
        {
            var x = x0 + rng.Next(Math.Max(1, x1 - x0));
            var y = y0 + rng.Next(Math.Max(1, y1 - y0));

            cells[y][x] = kinds[rng.Next(kinds.Length)];
        }
    }

    /// <summary>Base ground with a track worn across it, and scatter.</summary>
    private static void OpenGround(string[][] cells, TerrainKind kind, Rng rng)
    {
        // A walk from one edge to the other, drifting. Two cells thick where it drifts, so it
        // reads as worn rather than drawn with a ruler.
        var y = 2 + rng.Next(Height - 4);

        for (var x = 0; x < Width; x++)
        {
            cells[y][x] = kind.Feature;

            if (rng.Next(3) == 0)
            {
                var next = Math.Clamp(y + (rng.Next(2) == 0 ? -1 : 1), 1, Height - 2);
                cells[next][x] = kind.Feature;
                y = next;
            }
        }

        Furnish(cells, kind, rng, 0, Width, 0, Height);
    }

    /// <summary>One organic mass, grown by a walk rather than drawn as a shape.</summary>
    private static void Blob(string[][] cells, TerrainKind kind, Rng rng)
    {
        var x = 4 + rng.Next(Width - 8);
        var y = 2 + rng.Next(Height - 4);

        // Forty steps, revisiting cells often, so the result is a ragged mass of about half that
        // many cells - which is what the shipped marshes look like, and well inside the budget.
        for (var i = 0; i < 40; i++)
        {
            cells[y][x] = kind.Feature;

            x = Math.Clamp(x + rng.Next(3) - 1, 1, Width - 2);
            y = Math.Clamp(y + rng.Next(3) - 1, 1, Height - 2);
        }

        Furnish(cells, kind, rng, 0, Width, 0, Height);
    }

    /// <summary>
    /// Ground that gives out, from one edge.
    /// </summary>
    /// <remarks>
    /// The last two rows go entirely, and the two above them fray — the shipped rim rooms stop
    /// like that rather than at a straight line. The top rows are always left whole, which is what
    /// keeps the room standable and is why this shape needs no budget of its own.
    /// </remarks>
    private static void Dissolve(string[][] cells, TerrainKind kind, Rng rng)
    {
        for (var y = Height - 2; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                cells[y][x] = kind.Feature;
            }
        }

        for (var y = Height - 4; y < Height - 2; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                // Fraying more the further down and the further right, so the edge has a direction
                // instead of being noise.
                var chance = 2 + ((y - (Height - 4)) * 3) + (x * 4 / Width);

                if (rng.Next(10) < chance)
                {
                    cells[y][x] = kind.Feature;
                }
            }
        }
    }

    /// <summary>
    /// Turns tiles into rows and a legend, giving walls their corners.
    /// </summary>
    /// <remarks>
    /// The legend is built from what was actually drawn, so it can never name a tile the grid does
    /// not use or miss one it does — the two halves of what <c>CheckTerrain</c> asks.
    /// </remarks>
    private static RoomTerrain Render(string[][] cells)
    {
        var legend = new Dictionary<string, string>(StringComparer.Ordinal);
        var rows = new List<string>(Height);

        for (var y = 0; y < Height; y++)
        {
            var row = new char[Width];

            for (var x = 0; x < Width; x++)
            {
                var tile = cells[y][x];
                var glyph = tile == "wall" ? WallGlyph(cells, x, y) : Glyphs[tile];

                row[x] = glyph;
                legend[glyph.ToString()] = tile;
            }

            rows.Add(new string(row));
        }

        return new RoomTerrain(rows, legend);
    }

    /// <summary>Which piece of box-drawing this wall cell is, from the walls next to it.</summary>
    private static char WallGlyph(string[][] cells, int x, int y)
    {
        var up = IsWall(cells, x, y - 1);
        var down = IsWall(cells, x, y + 1);
        var left = IsWall(cells, x - 1, y);
        var right = IsWall(cells, x + 1, y);

        return (up, down, left, right) switch
        {
            (false, true, false, true) => '┌',
            (false, true, true, false) => '┐',
            (true, false, false, true) => '└',
            (true, false, true, false) => '┘',
            (true, true, _, _) => '│',
            _ => '─',
        };
    }

    private static bool IsWall(string[][] cells, int x, int y) =>
        x >= 0 && x < Width && y >= 0 && y < Height && cells[y][x] == "wall";

    /// <summary>How many cells of this map something could be drawn standing on.</summary>
    public static int OpenCells(RoomTerrain terrain)
    {
        ArgumentNullException.ThrowIfNull(terrain);

        return terrain.Grid
            .SelectMany(row => row)
            .Count(c => terrain.Legend.TryGetValue(c.ToString(), out var tile)
                && !RoomLayoutService.NonPlaceable.Contains(tile));
    }

    /// <summary>
    /// FNV-1a, written out because <c>string.GetHashCode</c> is randomised per process.
    /// </summary>
    /// <remarks>
    /// A hash that changes between runs would make "seeded by the room key" mean nothing: the same
    /// room would draw differently every time the server restarted, and the byte-identical
    /// regeneration WORLD.md relies on to keep diffs readable would be a fiction.
    /// </remarks>
    private static uint Hash(string text)
    {
        var hash = 2166136261u;

        foreach (var c in text)
        {
            hash ^= c;
            hash *= 16777619u;
        }

        return hash;
    }

    /// <summary>
    /// xorshift32, written out because <c>System.Random</c>'s sequence is not contracted to be
    /// stable between .NET versions — and it did change in .NET 6.
    /// </summary>
    private struct Rng(uint seed)
    {
        private uint _state = seed == 0 ? 1 : seed;

        public int Next(int bound)
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;

            return (int)(_state % (uint)bound);
        }
    }
}
