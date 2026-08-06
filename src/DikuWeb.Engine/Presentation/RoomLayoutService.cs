using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Items;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Protocol;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Presentation;

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
    private const int DefaultWidth = 11;
    private const int DefaultHeight = 5;
    private const string DefaultFloor = ".";

    /// <summary>Tiles nothing is ever drawn on. Purely an aesthetic choice.</summary>
    private static readonly HashSet<string> NonPlaceableTiles =
        new(StringComparer.OrdinalIgnoreCase) { "wall", "water" };

    private static readonly IReadOnlyDictionary<string, string> DefaultLegend =
        new Dictionary<string, string>(StringComparer.Ordinal) { [DefaultFloor] = "floor" };

    public MapPayload BuildMap(
        Room room,
        IReadOnlyList<PlayerActor> occupants,
        IReadOnlyList<Mob> mobs,
        IReadOnlyList<ItemInstance> items,
        PlayerActor viewer)
    {
        ArgumentNullException.ThrowIfNull(room);
        ArgumentNullException.ThrowIfNull(occupants);
        ArgumentNullException.ThrowIfNull(mobs);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(viewer);

        var terrain = ResolveTerrain(room);
        IReadOnlyDictionary<string, string> legend = room.HasGrid ? room.Legend : DefaultLegend;
        var height = terrain.Count;
        var width = height == 0 ? 0 : terrain[0].Length;

        var placeable = PlaceableCells(terrain, legend);
        var entities = new List<MapEntity>(occupants.Count + mobs.Count + items.Count);
        var taken = new HashSet<int>();

        // Players first, sorted so the result depends only on WHO is in the room, not on the
        // order they arrived. Two clients viewing the same room must draw it identically.
        foreach (var actor in occupants.OrderBy(o => o.EntityId, StringComparer.Ordinal))
        {
            var index = AssignCell(room.Key, actor.EntityId, placeable.Count, taken);
            if (index < 0)
            {
                continue;
            }

            var (x, y) = placeable[index];
            var isViewer = actor.CharacterId == viewer.CharacterId;

            entities.Add(new MapEntity(
                actor.EntityId,
                isViewer ? "@" : actor.Icon,
                x,
                y,
                isViewer ? "you" : actor.Name));
        }

        // Mobs, sorted by their ID for stability.
        foreach (var mob in mobs.OrderBy(m => m.Id))
        {
            var entityId = $"m_{mob.Id:N}";
            var index = AssignCell(room.Key, entityId, placeable.Count, taken);
            if (index < 0)
            {
                continue;
            }

            var (x, y) = placeable[index];
            var displayName = string.IsNullOrEmpty(mob.TemplateName) ? mob.TemplateKey : mob.TemplateName;
            var icon = string.IsNullOrEmpty(mob.TemplateName) ? mob.TemplateKey[0].ToString() : displayName[0].ToString();
            entities.Add(new MapEntity(entityId, icon, x, y, displayName));
        }

        // Items, sorted by their ID for stability.
        foreach (var item in items.OrderBy(i => i.Id))
        {
            var entityId = $"i_{item.Id:N}";
            var index = AssignCell(room.Key, entityId, placeable.Count, taken);
            if (index < 0)
            {
                continue;
            }

            var (x, y) = placeable[index];
            var displayName = string.IsNullOrEmpty(item.TemplateName) ? item.TemplateKey : item.TemplateName;
            entities.Add(new MapEntity(entityId, item.Icon, x, y, displayName));
        }

        return new MapPayload(width, height, terrain, entities);
    }

    private static IReadOnlyList<string> ResolveTerrain(Room room)
    {
        if (!room.HasGrid)
        {
            // PLAN.md §4.4: grid art is optional, so a room without it renders a plain
            // rectangle. Art is an upgrade, not a tax on world building.
            return [.. Enumerable.Repeat(new string(DefaultFloor[0], DefaultWidth), DefaultHeight)];
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

        for (var y = 0; y < terrain.Count; y++)
        {
            for (var x = 0; x < terrain[y].Length; x++)
            {
                var glyph = terrain[y][x].ToString();

                // An unmapped glyph is a builder mistake; treat it as placeable rather than
                // excluding it, so a typo cannot make a room impossible to stand in.
                if (legend.TryGetValue(glyph, out var tile) && NonPlaceableTiles.Contains(tile))
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
    /// Deterministic placement: hash the entity into a candidate cell, then linear-probe for
    /// the next free one. Because it is a pure function of (room, entity, occupancy), the
    /// same kobold sits in the same spot across reconnects and server restarts, and nothing
    /// has to be persisted (PLAN.md §4.3).
    /// </summary>
    private static int AssignCell(RoomKey roomKey, string entityId, int cellCount, HashSet<int> taken)
    {
        if (cellCount == 0)
        {
            return -1;
        }

        var start = (int)(StableHash(roomKey.ToString(), entityId) % (uint)cellCount);

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
    private static uint StableHash(string a, string b)
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
