using DikuWeb.Server.Building;

namespace DikuWeb.Server.Tests.Building;

/// <summary>
/// Every kind draws a room the rest of the system will accept.
/// </summary>
/// <remarks>
/// The original generator was a Python script that drew 224 rooms and was never committed, so
/// there is nothing to compare against but the rules. Those are enough, and they are the rules
/// that matter: <c>BundleValidator.CheckTerrain</c> refuses a ragged grid, a glyph with no legend,
/// a legend entry nothing draws, or a room with under forty cells to stand on.
/// </remarks>
public sealed class TerrainGeneratorTests
{
    /// <summary>Room keys to draw with, standing in for a zone's worth of variety.</summary>
    private static readonly string[] Keys =
    [
        "ossara.gatetown.the-market",
        "ossara.gatetown.gate-road",
        "grask.stiltmarsh.the-black-reach",
        "azhen.serrivet.the-rim-platform",
        "nemhal.keshvaun.the-long-stair",
        "the-unlit.the-regard.the-standing-floor",
    ];

    public static TheoryData<string> AllKinds()
    {
        var data = new TheoryData<string>();

        foreach (var kind in TerrainGenerator.Kinds)
        {
            data.Add(kind.Key);
        }

        return data;
    }

    /// <summary>Twenty-one kinds, matching the twenty-one tile-sets the shipped rooms use.</summary>
    [Fact]
    public void There_are_twenty_one_kinds()
    {
        Assert.Equal(21, TerrainGenerator.Kinds.Count);
        Assert.Equal(
            TerrainGenerator.Kinds.Count,
            TerrainGenerator.Kinds.Select(k => k.Key).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// The two the world design leans on rather than decorates with.
    /// </summary>
    /// <remarks>
    /// WORLD.md §10.1: <c>rim</c> is where the land stops and <c>standing</c> is the Unlit — a
    /// floor with void all round it. Renaming either would quietly detach the map from the prose
    /// that chose it.
    /// </remarks>
    [Theory]
    [InlineData("rim")]
    [InlineData("standing")]
    public void The_load_bearing_kinds_keep_their_names(string key)
    {
        Assert.NotNull(TerrainGenerator.Find(key));
    }

    [Theory]
    [MemberData(nameof(AllKinds))]
    public void Every_kind_draws_the_default_size(string kind)
    {
        foreach (var key in Keys)
        {
            var terrain = TerrainGenerator.Generate(kind, key);

            Assert.Equal(TerrainGenerator.Height, terrain.Grid.Count);
            Assert.All(terrain.Grid, row => Assert.Equal(TerrainGenerator.Width, row.Length));
        }
    }

    /// <summary>
    /// Every glyph drawn is in the legend, and every legend entry is drawn.
    /// </summary>
    /// <remarks>
    /// Both directions, because <c>CheckTerrain</c> checks both: the first is an error and the
    /// second a warning, and a generator that produced either would be making work for whoever
    /// ran validate next.
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllKinds))]
    public void Every_glyph_is_legended_and_every_legend_entry_is_drawn(string kind)
    {
        foreach (var key in Keys)
        {
            var terrain = TerrainGenerator.Generate(kind, key);
            var drawn = terrain.Grid.SelectMany(row => row).Select(c => c.ToString()).ToHashSet(StringComparer.Ordinal);

            Assert.Empty(drawn.Except(terrain.Legend.Keys, StringComparer.Ordinal));
            Assert.Empty(terrain.Legend.Keys.Except(drawn, StringComparer.Ordinal));
        }
    }

    /// <summary>
    /// Every kind leaves somewhere to stand.
    /// </summary>
    /// <remarks>
    /// The check that matters most, and the one WORLD.md singles out: entities are placed only on
    /// open ground and are <em>not drawn at all</em> when there is none, so a room under the floor
    /// is a room whose occupants vanish. Swept over many seeds rather than one, because the shapes
    /// are random and a budget that holds for one seed is not a budget.
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllKinds))]
    public void Every_kind_leaves_room_to_stand(string kind)
    {
        for (var i = 0; i < 200; i++)
        {
            var terrain = TerrainGenerator.Generate(kind, $"zone.room-{i}");
            var open = TerrainGenerator.OpenCells(terrain);

            Assert.True(
                open >= TerrainGenerator.MinOpenCells,
                $"'{kind}' seed {i} leaves {open} cells to stand on, under the "
                + $"{TerrainGenerator.MinOpenCells} minimum.");
        }
    }

    /// <summary>
    /// Every glyph is one cell wide.
    /// </summary>
    /// <remarks>
    /// <c>RoomLayoutService</c> indexes terrain rows per <c>char</c>, so a surrogate pair — any
    /// emoji — would be split across two cells and neither half would draw.
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllKinds))]
    public void Every_glyph_is_a_single_character(string kind)
    {
        var terrain = TerrainGenerator.Generate(kind, Keys[0]);

        Assert.All(terrain.Legend.Keys, glyph =>
        {
            Assert.Single(glyph);
            Assert.False(char.IsSurrogate(glyph[0]));
        });
    }

    /// <summary>
    /// The same room always draws the same map.
    /// </summary>
    /// <remarks>
    /// The property the whole design rests on. WORLD.md: a random seed would rewrite all 224 rooms
    /// on every run and no diff could ever be read.
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllKinds))]
    public void The_same_room_draws_the_same_map(string kind)
    {
        foreach (var key in Keys)
        {
            Assert.Equal(
                TerrainGenerator.Generate(kind, key).Grid,
                TerrainGenerator.Generate(kind, key).Grid);
        }
    }

    /// <summary>And different rooms do not all draw the same map.</summary>
    /// <remarks>
    /// The other half: a seed that were ignored would pass the test above perfectly.
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllKinds))]
    public void Different_rooms_draw_different_maps(string kind)
    {
        var drawn = Keys
            .Select(key => string.Join('\n', TerrainGenerator.Generate(kind, key).Grid))
            .Distinct(StringComparer.Ordinal)
            .Count();

        // Not all six: two seeds landing on the same enclosed layout is legitimate, since an
        // enclosed room has little to vary. More than one is what says the seed is read at all.
        Assert.True(drawn > 1, $"'{kind}' drew the same map for every room key.");
    }

    /// <summary>
    /// Pinned output, so a change to the hash or the generator has to be deliberate.
    /// </summary>
    /// <remarks>
    /// <b>This is the test that makes "byte-identical regeneration" mean something.</b> Both the
    /// hash and the RNG are written out in <c>TerrainGenerator</c> precisely because the framework
    /// versions of each are not stable — <c>string.GetHashCode</c> is randomised per process and
    /// <c>System.Random</c>'s sequence changed in .NET 6. Neither would fail a test; both would
    /// silently redraw every room in the world. This notices.
    /// <para>
    /// If it fails after a deliberate change to the drawing, the fix is to update the expectation —
    /// and to know that doing so rewrites every generated room in <c>content/</c>.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_drawing_is_pinned()
    {
        var terrain = TerrainGenerator.Generate("hall", "ossara.gatetown.the-market");

        Assert.Equal(
            [
                "┌───────────────────┐",
                "│...................│",
                "│..▮...▮...▮...▮....│",
                "│...................│",
                "│...................│",
                "│...................│",
                "│.▮...▮...▮...▮...▮.│",
                "│...................│",
                "└───────────────────┘",
            ],
            terrain.Grid);
    }

    /// <summary>An unknown kind is refused rather than drawn as something else.</summary>
    [Fact]
    public void An_unknown_kind_is_refused()
    {
        Assert.Throws<ArgumentException>(() => TerrainGenerator.Generate("swamp-of-sadness", "a.b.c"));
    }
}
