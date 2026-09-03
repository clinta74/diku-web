using Muwbta.Domain.Inhabitants;
using Muwbta.Domain.Items;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Protocol;
using Muwbta.Engine.World;

namespace Muwbta.Engine.Presentation;

/// <summary>
/// The only place in the codebase where an entity has coordinates (PLAN.md §4.2).
///
/// The room map is cosmetic. Movement is room-to-room, combat has no range, and interaction
/// works on anything in the room regardless of where it is drawn. This service sits
/// downstream of the rules and is consulted only when building map events - game logic
/// cannot read a position because Domain has no coordinate field to read.
///
/// An architecture test enforces that. If you find yourself wanting to call this from a
/// command handler, the design has drifted.
/// </summary>
public sealed class RoomLayoutService
{
    /// <summary>
    /// The rectangle a room with no authored art renders as. Doubled from 11x5, to match the
    /// size the starter rooms are drawn at - a room that fell back to the old size looked
    /// cramped beside its neighbours rather than merely plain.
    /// </summary>
    private const int DefaultWidth = 21;
    private const int DefaultHeight = 9;
    private const string DefaultFloor = ".";

    /// <summary>
    /// Tiles nothing is ever drawn on.
    /// </summary>
    /// <remarks>
    /// Cosmetic, but it is what stops a rat being drawn standing on the altar or inside the
    /// forge. Anything solid enough that a builder drew it as an object belongs here; open
    /// ground - floor, grass, path, rubble, stairs - does not.
    ///
    /// <b>"void" is the odd one, and it is the reason this list is not called "solid".</b> The
    /// Reaches are shards with nothing between them, so a rim room draws the edge where the land
    /// stops. Void is the opposite of solid and just as un-standable, and the question this set
    /// answers is "may something be drawn here", not "is it made of stone".
    /// </remarks>
    /// <summary>
    /// The same set, for anything that needs to ask the question without drawing a room.
    /// </summary>
    /// <remarks>
    /// Exposed for the bundle validator, which checks that a room leaves somewhere to stand — a
    /// room that does not renders with its occupants missing entirely, silently. That check used to
    /// recover this list by running a regex over this file, which is a second transcription and so
    /// a second thing to get wrong.
    /// </remarks>
    public static IReadOnlySet<string> NonPlaceable => NonPlaceableTiles;

    private static readonly HashSet<string> NonPlaceableTiles =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "wall",
            "water",
            "tree",
            "oak",
            "table",
            "bench",
            "bar",
            "altar",
            "forge",
            "anvil",
            "well",
            "millstone",

            // The Reaches vocabulary.
            "void",
            "pillar",
            "rock",
            "crate",
            "brazier",
        };

    private static readonly IReadOnlyDictionary<string, string> DefaultLegend =
        new Dictionary<string, string>(StringComparer.Ordinal) { [DefaultFloor] = "floor" };

    /// <summary>The plain rectangle an art-less room renders as, built once and shared.</summary>
    private static readonly string[] BlankTerrain =
        [.. Enumerable.Repeat(new string(DefaultFloor[0], DefaultWidth), DefaultHeight)];

    /// <summary>
    /// The room, drawn once, exactly the same for everybody standing in it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Nothing here depends on who is looking, and that is the design rather than an
    /// optimisation.</b> Placement is a function of the room, the entity and the occupancy;
    /// nobody is marked as "you"; and the result is a single immutable payload that goes into
    /// every occupant's channel by reference. Marking the viewer is a rendering decision and the
    /// client makes it, because the client is the only party that knows who is holding the screen.
    /// </para>
    /// <para>
    /// It was not always so, and the history is the justification. This used to take a viewer, and
    /// <c>PlayerView.RefreshRoom</c> called it once per occupant — so a room holding sixty people
    /// sorted its occupants sixty times, walked the grid sixty times and hashed every entity into
    /// a cell sixty times, to change one icon and one label on each pass. Hoisting the layout out
    /// of that loop stopped the game loop missing three pulses in ten at two hundred crowded
    /// sessions, but left a cheaper quadratic behind: an N-element array copied per viewer, to
    /// patch a single element. Only moving the mark to the client removes the last of it, which is
    /// why the protocol carries an unmarked room and the client draws its own <c>@</c>
    /// (PLAN.md §11).
    /// </para>
    /// </remarks>
    public MapPayload BuildMap(
        Room room,
        IReadOnlyList<PlayerActor> occupants,
        IReadOnlyList<Mob> mobs,
        IReadOnlyList<ItemInstance> items)
    {
        ArgumentNullException.ThrowIfNull(room);
        ArgumentNullException.ThrowIfNull(occupants);
        ArgumentNullException.ThrowIfNull(mobs);
        ArgumentNullException.ThrowIfNull(items);

        var terrain = ResolveTerrain(room);
        IReadOnlyDictionary<string, string> legend = room.HasGrid ? room.Legend : DefaultLegend;
        var height = terrain.Count;
        var width = height == 0 ? 0 : terrain[0].Length;

        var placeable = PlaceableCells(terrain, legend);
        var entities = new List<MapEntity>(occupants.Count + mobs.Count + items.Count);
        var taken = new HashSet<int>();

        // Rendered once rather than inside all three loops below. RoomKey wraps a string and its
        // ToString allocates, so this used to cost one string per entity in the room.
        var roomKey = room.Key.ToString();

        // Players first, sorted so the result depends only on WHO is in the room, not on the
        // order they arrived. Two clients viewing the same room must draw it identically.
        foreach (var actor in occupants.OrderBy(o => o.EntityId, StringComparer.Ordinal))
        {
            var index = AssignCell(roomKey, actor.EntityId, placeable.Count, taken);
            if (index < 0)
            {
                continue;
            }

            var (x, y) = placeable[index];

            entities.Add(new MapEntity(actor.EntityId, actor.Icon, x, y, actor.Name, "player"));
        }

        // Mobs, sorted by their ID for stability.
        foreach (var mob in mobs.OrderBy(m => m.Id))
        {
            var entityId = PrefixedId('m', mob.Id);
            var index = AssignCell(roomKey, entityId, placeable.Count, taken);
            if (index < 0)
            {
                continue;
            }

            var (x, y) = placeable[index];
            var icon = mob.MapGlyph;
            entities.Add(new MapEntity(entityId, icon, x, y, MobLabel.For(mobs, mob), "mob"));
        }

        // Items, sorted by their ID for stability.
        foreach (var item in items.OrderBy(i => i.Id))
        {
            var entityId = PrefixedId('i', item.Id);
            var index = AssignCell(roomKey, entityId, placeable.Count, taken);
            if (index < 0)
            {
                continue;
            }

            var (x, y) = placeable[index];
            entities.Add(new MapEntity(entityId, item.Icon, x, y, item.DisplayName, "item"));
        }

        // An array rather than the List, so Personalise can copy it with Array.Copy rather than
        // walking it element by element. Everything downstream reads it as IReadOnlyList anyway.
        return new MapPayload(width, height, terrain, entities.ToArray());
    }

    /// <summary>
    /// A prefixed entity id, formatted on the stack rather than through an interpolation.
    /// </summary>
    /// <remarks>
    /// A prefix, an underscore and a Guid in "N" form is thirty-four characters, all known at
    /// compile time — so the whole id is written into a stack buffer and copied out as one string,
    /// where the interpolated form allocated a handler and an intermediate. It runs once per mob
    /// and once per item on every map build.
    /// </remarks>
    private static string PrefixedId(char prefix, Guid id)
    {
        const int GuidLength = 32;

        Span<char> buffer = stackalloc char[GuidLength + 2];
        buffer[0] = prefix;
        buffer[1] = '_';

        // "N" is thirty-two hex digits with no punctuation, so this always fits. The result is
        // checked rather than discarded because a silent false would yield a two-character id,
        // and every mob in the room would then hash into the same cell.
        return id.TryFormat(buffer[2..], out var written, "N")
            ? new string(buffer[..(2 + written)])
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{prefix}_{id:N}");
    }

    private static IReadOnlyList<string> ResolveTerrain(Room room)
    {
        if (!room.HasGrid)
        {
            // PLAN.md §4.4: grid art is optional, so a room without it renders a plain
            // rectangle. Art is an upgrade, not a tax on world building.
            //
            // Shared rather than rebuilt: the rows are immutable strings and the array is only
            // ever read, so every art-less room in the world can hand out the same one. Most
            // rooms are art-less, which made this the most-allocated object on the map path.
            return BlankTerrain;
        }

        // A ragged grid is a builder mistake that must not crash the loop (PLAN.md §7.4),
        // so pad every row to the widest one instead of trusting the input.
        var width = room.Grid.Max(row => row.Length);
        return [.. room.Grid.Select(row => row.PadRight(width))];
    }

    private static List<(int X, int Y)> PlaceableCells(
        IReadOnlyList<string> terrain,
        IReadOnlyDictionary<string, string> legend)
    {
        var cells = new List<(int X, int Y)>();

        // The legend is resolved to a set of blocked characters once, rather than per cell. The
        // inner loop used to call ToString() on every character of every row purely to look it up
        // in a string-keyed dictionary - one allocation per cell, so roughly two hundred per map
        // on the default grid. A legend has a handful of entries; a grid has hundreds of cells.
        var blocked = BlockedGlyphs(legend);

        for (var y = 0; y < terrain.Count; y++)
        {
            var row = terrain[y].AsSpan();

            for (var x = 0; x < row.Length; x++)
            {
                // An unmapped glyph is a builder mistake; treat it as placeable rather than
                // excluding it, so a typo cannot make a room impossible to stand in.
                if (blocked.Contains(row[x]))
                {
                    continue;
                }

                cells.Add((x, y));
            }
        }

        // A room drawn as solid wall would otherwise have nowhere to stand, and everyone in
        // it would vanish from the map. Cosmetic correctness loses to being visible.
        if (cells.Count == 0)
        {
            for (var y = 0; y < terrain.Count; y++)
            {
                for (var x = 0; x < terrain[y].Length; x++)
                {
                    cells.Add((x, y));
                }
            }
        }

        return cells;
    }

    /// <summary>
    /// The characters a legend marks as un-standable.
    /// </summary>
    /// <remarks>
    /// Only single-character keys can ever match, because the caller looks up one grid character
    /// at a time — a multi-character legend key was already dead weight before this and is simply
    /// skipped here rather than silently treated as a prefix.
    /// </remarks>
    private static HashSet<char> BlockedGlyphs(IReadOnlyDictionary<string, string> legend)
    {
        var blocked = new HashSet<char>();

        foreach (var (glyph, tile) in legend)
        {
            if (glyph.Length == 1 && NonPlaceableTiles.Contains(tile))
            {
                blocked.Add(glyph[0]);
            }
        }

        return blocked;
    }

    /// <summary>
    /// Deterministic placement: hash the entity into a candidate cell, then linear-probe for
    /// the next free one. Because it is a pure function of (room, entity, occupancy), the
    /// same kobold sits in the same spot across reconnects and server restarts, and nothing
    /// has to be persisted (PLAN.md §4.3).
    /// </summary>
    private static int AssignCell(
        ReadOnlySpan<char> roomKey,
        ReadOnlySpan<char> entityId,
        int cellCount,
        HashSet<int> taken)
    {
        if (cellCount == 0)
        {
            return -1;
        }

        var start = (int)(StableHash(roomKey, entityId) % (uint)cellCount);

        for (var offset = 0; offset < cellCount; offset++)
        {
            var index = (start + offset) % cellCount;
            if (taken.Add(index))
            {
                return index;
            }
        }

        // More occupants than cells - a small room during a crowd. Stack rather than drop:
        // an overlapping icon is a cosmetic blemish, but an occupant missing from the map
        // while standing in the room reads as a bug to the player.
        return start;
    }

    /// <summary>
    /// FNV-1a. Emphatically not string.GetHashCode(): .NET randomises string hash codes per
    /// process, so using it would reshuffle every room on every server restart - the exact
    /// opposite of the stability this design depends on.
    /// </summary>
    /// <remarks>
    /// Takes spans rather than strings so callers can hand it slices without materialising them.
    /// The arithmetic is unchanged and so are the cell assignments it produces: the stability this
    /// comment is about is a promise to players, and a placement that shifted would be visible as
    /// the furniture rearranging itself.
    /// </remarks>
    private static uint StableHash(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        const uint OffsetBasis = 2166136261;
        const uint Prime = 16777619;

        var hash = OffsetBasis;

        foreach (var c in a)
        {
            hash = (hash ^ c) * Prime;
        }

        hash = (hash ^ '|') * Prime;

        foreach (var c in b)
        {
            hash = (hash ^ c) * Prime;
        }

        return hash;
    }
}
