using Muwbta.Domain.Worlds;
using Muwbta.Engine.Protocol;
using Muwbta.Engine.Tests.Infrastructure;

namespace Muwbta.Engine.Tests.Presentation;

/// <summary>
/// A mob is drawn as its authored icon (BUGS.md #10).
/// </summary>
/// <remarks>
/// <c>MobTemplate.Icon</c> is <c>required</c>, persisted, exposed in the builder, and authored
/// across all 68 templates in <c>content/</c> on a deliberate scheme — <c>r</c> vermin, <c>c</c>
/// flyers, <c>d</c> canines, <c>@</c> named NPCs. Nothing read it. <c>Mob</c> had no icon field at
/// all, so unlike <c>ItemInstance</c> nothing even copied it at spawn, and both render paths took
/// the first letter of the display name instead.
///
/// Because a mob name almost always begins with its article, that made the whole map a field of
/// lowercase <c>a</c>. Item icons were read correctly the entire time, which is what made the map
/// look deliberate rather than broken — the failure mode this codebase keeps meeting.
/// </remarks>
public sealed class MobIconTests
{
    private static readonly RoomKey Room = RoomKey.Parse("test.zone.west");

    private static WorldHarness Loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return harness;
    }

    private static IReadOnlyList<MapEntity> MapOf(WorldHarness harness, World.PlayerActor actor)
    {
        harness.View.SendRoom(harness.World, actor, verbose: true);
        var map = (MapPayload)harness.Drain(actor).Last(e => e.Type == EventTypes.Map).Payload;
        return map.Entities;
    }

    private static IReadOnlyList<ContentEntry> ContentsOf(WorldHarness harness, World.PlayerActor actor)
    {
        harness.View.SendRoom(harness.World, actor, verbose: true);
        var contents = (ContentsPayload)harness.Drain(actor)
            .Last(e => e.Type == EventTypes.Contents).Payload;
        return contents.Occupants;
    }

    [Fact]
    public void The_map_draws_the_authored_icon()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        harness.AddMob("crow", Room, name: "a terrace crow", icon: "c");

        var mob = MapOf(harness, kael).Single(e => e.Type == "mob");

        Assert.Equal("c", mob.Icon);
    }

    /// <summary>
    /// The contents list is the map's legend, so the two must agree or the key explains nothing.
    /// </summary>
    [Fact]
    public void The_contents_list_agrees_with_the_map()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        harness.AddMob("crow", Room, name: "a terrace crow", icon: "c");

        var onMap = MapOf(harness, kael).Single(e => e.Type == "mob").Icon;
        var inList = ContentsOf(harness, kael).Single(e => e.Keyword == "crow").Icon;

        Assert.Equal(onMap, inList);
    }

    /// <summary>
    /// The bug, stated as the thing that must not happen again.
    /// </summary>
    [Fact]
    public void Two_differently_iconed_mobs_do_not_draw_the_same_glyph()
    {
        // Both names begin with "a ", which is true of nearly every mob in the Reaches. Under the
        // old first-letter rule these were indistinguishable on the map.
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        harness.AddMob("crow", Room, name: "a terrace crow", icon: "c");
        harness.AddMob("hound", Room, name: "a hollow hound", icon: "d");

        var icons = MapOf(harness, kael).Where(e => e.Type == "mob").Select(e => e.Icon).ToList();

        Assert.Equal(2, icons.Count);
        Assert.Equal(2, icons.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// An instance built without one still draws something recognisable rather than nothing.
    /// </summary>
    /// <remarks>
    /// The same trade <c>DisplayName</c> makes for a nameless mob. A blank glyph would punch a hole
    /// in the grid and read as a rendering fault rather than as missing content.
    /// </remarks>
    [Fact]
    public void A_mob_with_no_icon_falls_back_to_its_first_letter()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        harness.AddMob("rat", Room, name: "wharf rat", icon: "");

        var mob = MapOf(harness, kael).Single(e => e.Type == "mob");

        Assert.Equal("w", mob.Icon);
    }

    /// <summary>A multi-character icon is trimmed rather than breaking the grid's alignment.</summary>
    [Fact]
    public void An_over_long_icon_is_cut_to_one_cell()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        harness.AddMob("rat", Room, name: "a wharf rat", icon: "rat");

        Assert.Equal("r", MapOf(harness, kael).Single(e => e.Type == "mob").Icon);
    }
}
