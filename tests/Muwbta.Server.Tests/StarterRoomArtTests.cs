using System.Reflection;
using Muwbta.Domain.Worlds;
using Muwbta.Persistence.Seeding;

namespace Muwbta.Server.Tests;

/// <summary>
/// The starter zone's map art.
/// </summary>
/// <remarks>
/// Grid art is hand-drawn, so the failure modes are typos: a row a character short, a glyph with
/// no legend entry, a character outside the basic plane. None of those throw — a ragged grid is
/// padded, an unmapped glyph is treated as floor, and a surrogate pair is split across two cells
/// and drawn as mojibake. All three are silent, which is what makes them worth a test.
/// </remarks>
public sealed class StarterRoomArtTests
{
    /// <summary>The seeded rooms, read off the seeder's private table.</summary>
    private static IReadOnlyList<(string Slug, string[] Grid, Dictionary<string, string> Legend)> Rooms()
    {
        var field = typeof(StarterWorldSeeder)
            .GetField("Rooms", BindingFlags.NonPublic | BindingFlags.Static)!;

        var seeds = (System.Collections.IEnumerable)field.GetValue(null)!;
        var rooms = new List<(string, string[], Dictionary<string, string>)>();

        foreach (var seed in seeds)
        {
            var type = seed.GetType();
            rooms.Add((
                (string)type.GetProperty("Slug")!.GetValue(seed)!,
                (string[])type.GetProperty("Grid")!.GetValue(seed)!,
                (Dictionary<string, string>)type.GetProperty("Legend")!.GetValue(seed)!));
        }

        return rooms;
    }

    [Fact]
    public void Every_room_has_art()
    {
        // Six rooms shipped with an empty grid and rendered as a blank rectangle.
        Assert.All(Rooms(), room =>
            Assert.False(room.Grid.Length == 0, $"{room.Slug} has no grid."));
    }

    [Fact]
    public void Every_grid_is_rectangular()
    {
        foreach (var (slug, grid, _) in Rooms())
        {
            var widths = grid.Select(row => row.Length).Distinct().ToList();
            Assert.True(widths.Count == 1, $"{slug} has ragged rows: {string.Join(", ", widths)}.");
        }
    }

    [Fact]
    public void Every_grid_is_the_full_size()
    {
        // Smaller art still renders, but it reads as cramped beside its neighbours.
        foreach (var (slug, grid, _) in Rooms())
        {
            Assert.True(grid.Length == 9, $"{slug} is {grid.Length} rows tall, not 9.");
            Assert.True(grid[0].Length == 21, $"{slug} is {grid[0].Length} wide, not 21.");
        }
    }

    [Fact]
    public void Every_glyph_has_a_legend_entry()
    {
        // An unmapped glyph is treated as placeable floor, so a wall a builder can stand in
        // looks like a placement bug rather than a missing legend key.
        foreach (var (slug, grid, legend) in Rooms())
        {
            var unmapped = grid
                .SelectMany(row => row)
                .Select(c => c.ToString())
                .Distinct(StringComparer.Ordinal)
                .Where(glyph => !legend.ContainsKey(glyph))
                .ToList();

            Assert.True(unmapped.Count == 0, $"{slug} uses unmapped glyphs: {string.Join(" ", unmapped)}.");
        }
    }

    [Fact]
    public void No_legend_entry_is_unused()
    {
        // A leftover key is harmless but means the art moved on without the legend.
        foreach (var (slug, grid, legend) in Rooms())
        {
            var drawn = grid.SelectMany(row => row).Select(c => c.ToString()).ToHashSet(StringComparer.Ordinal);
            var unused = legend.Keys.Where(k => !drawn.Contains(k)).ToList();

            Assert.True(unused.Count == 0, $"{slug} declares unused glyphs: {string.Join(" ", unused)}.");
        }
    }

    /// <summary>
    /// <c>RoomLayoutService</c> indexes terrain per <c>char</c>, so a glyph outside the basic
    /// plane would be split across two cells and neither half would render.
    /// </summary>
    [Fact]
    public void Every_glyph_is_a_single_basic_plane_character()
    {
        foreach (var (slug, grid, legend) in Rooms())
        {
            foreach (var row in grid)
            {
                Assert.False(
                    row.Any(char.IsSurrogate),
                    $"{slug} uses a character outside the basic plane, which cannot be drawn.");
            }

            Assert.All(legend.Keys, key =>
                Assert.True(key.Length == 1, $"{slug} has a multi-character legend key '{key}'."));
        }
    }

    [Fact]
    public void Every_room_has_somewhere_to_stand()
    {
        // A room drawn as solid furniture has no placeable cell, and everyone in it would
        // stack on the fallback. The service survives that; the room would still be wrong.
        var service = new Engine.Presentation.RoomLayoutService();

        foreach (var (slug, grid, legend) in Rooms())
        {
            var room = new Room
            {
                Key = RoomKey.Create("test", "zone", slug),
                ZoneKey = "test.zone",
                Title = slug,
                Description = string.Empty,
                Grid = [.. grid],
                Legend = new Dictionary<string, string>(legend, StringComparer.Ordinal),
            };

            var map = service.BuildMap(room, [], [], []);

            Assert.Equal(21, map.W);
            Assert.Equal(9, map.H);
        }
    }
}
