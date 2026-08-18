using DikuWeb.Domain.Items;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Protocol;
using DikuWeb.Engine.Tests.Infrastructure;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Tests.Presentation;

/// <summary>
/// The <c>dark</c> flag, and the items that answer it (PLAN.md §4.10).
/// </summary>
/// <remarks>
/// The flag was registered in Phase 5 and read by nothing until now, so seven rooms in
/// <c>content/</c> were authored dark and rendered exactly like every other room. These tests are
/// as much about the withholding being <em>complete</em> as about it happening at all: a room whose
/// description is hidden but whose contents list still names everybody in it is not dark, it is
/// merely terse.
/// </remarks>
public sealed class DarkRoomTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");
    private static readonly RoomKey East = RoomKey.Parse("test.zone.east");

    private static WorldHarness Loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return harness;
    }

    private static void Darken(WorldHarness harness, RoomKey roomKey) =>
        harness.World.FindRoom(roomKey)!.Flags.Set(RoomFlags.Dark.Key, true);

    private static ItemTemplate Lantern(WorldHarness harness, bool lights = true) =>
        harness.AddItemTemplate(new ItemTemplate
        {
            Key = lights ? "a-lantern" : "a-cold-lantern",
            Name = lights ? "a brass lantern" : "an unlit lantern",
            Icon = "(",
            Slots = [ItemSlot.OffHand],
            IsLightSource = lights,
        });

    private static string Prose(WorldHarness harness, PlayerActor actor)
    {
        harness.View.SendRoom(harness.World, actor, verbose: true);
        return harness.DrainText(actor);
    }

    private static RoomPayload Room(WorldHarness harness, PlayerActor actor)
    {
        harness.View.SendRoom(harness.World, actor, verbose: true);
        return (RoomPayload)harness.Drain(actor).Last(e => e.Type == EventTypes.Room).Payload;
    }

    private static ContentsPayload Contents(WorldHarness harness, PlayerActor actor)
    {
        harness.View.SendRoom(harness.World, actor, verbose: true);
        return (ContentsPayload)harness.Drain(actor).Last(e => e.Type == EventTypes.Contents).Payload;
    }

    // -----------------------------------------------------------------------
    // What the dark takes
    // -----------------------------------------------------------------------

    [Fact]
    public void An_unflagged_room_reads_normally()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", West);

        var room = Room(harness, kael);

        Assert.Equal("The west room", room.Title);
        Assert.NotEqual(string.Empty, room.Description);
    }

    [Fact]
    public void A_dark_room_withholds_its_name_and_its_description()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", West);
        Darken(harness, West);

        var room = Room(harness, kael);

        Assert.Equal("Darkness", room.Title);
        Assert.Equal(string.Empty, room.Description);
    }

    [Fact]
    public void A_dark_room_says_why_there_is_nothing_to_read()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", West);
        Darken(harness, West);

        Assert.Contains("pitch black", Prose(harness, kael), StringComparison.Ordinal);
    }

    /// <summary>
    /// Exits are the one thing the dark keeps, deliberately: walking is the only way out, and on a
    /// phone the exit pad is drawn from this list.
    /// </summary>
    [Fact]
    public void A_dark_room_still_says_which_ways_out_there_are()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", West);
        Darken(harness, West);

        Assert.NotEmpty(Room(harness, kael).Exits);
    }

    [Fact]
    public void A_dark_room_hides_the_people_in_it()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", West);
        harness.AddPlayer("Mira", West);
        Darken(harness, West);

        var contents = Contents(harness, kael);

        Assert.DoesNotContain(contents.Occupants, e => e.Label.Contains("Mira", StringComparison.Ordinal));
        Assert.DoesNotContain("Mira is here", Prose(harness, kael), StringComparison.Ordinal);
    }

    [Fact]
    public void A_dark_room_hides_the_mobs_and_the_loot()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", West);
        harness.AddMob("rat", West);
        harness.DropItemInRoom(Lantern(harness, lights: false), West);
        Darken(harness, West);

        var contents = Contents(harness, kael);

        Assert.Empty(contents.Items);
        Assert.DoesNotContain(contents.Occupants, e => e.Keyword == "rat");
    }

    /// <summary>
    /// The grid keeps its shape so the panel does not jump, and carries nothing.
    /// </summary>
    [Fact]
    public void A_dark_room_draws_a_blank_map()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", West);
        harness.AddMob("rat", West);

        harness.View.SendRoom(harness.World, kael, verbose: true);
        var lit = (MapPayload)harness.Drain(kael).Last(e => e.Type == EventTypes.Map).Payload;

        Darken(harness, West);
        harness.View.SendRoom(harness.World, kael, verbose: true);
        var unlit = (MapPayload)harness.Drain(kael).Last(e => e.Type == EventTypes.Map).Payload;

        Assert.Equal(lit.W, unlit.W);
        Assert.Equal(lit.H, unlit.H);
        Assert.Equal(lit.Terrain.Count, unlit.Terrain.Count);
        Assert.All(unlit.Terrain, row => Assert.True(row.All(c => c == ' ')));
        Assert.Empty(unlit.Entities);
    }

    // -----------------------------------------------------------------------
    // What gives it back
    // -----------------------------------------------------------------------

    [Fact]
    public void An_equipped_light_source_lights_the_room()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", West);
        Darken(harness, West);
        harness.Equip(kael, Lantern(harness), ItemSlot.OffHand);

        Assert.Equal("The west room", Room(harness, kael).Title);
    }

    /// <summary>Any slot. A helm with a lamp on it is a lamp.</summary>
    [Fact]
    public void A_light_source_works_from_any_slot()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", West);
        Darken(harness, West);

        var helm = harness.AddItemTemplate(new ItemTemplate
        {
            Key = "a-lamped-helm",
            Name = "a helm with a lamp on it",
            Icon = "^",
            Slots = [ItemSlot.Head],
            IsLightSource = true,
        });

        harness.Equip(kael, helm, ItemSlot.Head);

        Assert.Equal("The west room", Room(harness, kael).Title);
    }

    /// <summary>A light you have not taken out is not lit.</summary>
    [Fact]
    public void A_light_source_in_the_pack_does_nothing()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", West);
        Darken(harness, West);
        harness.GiveItem(kael, Lantern(harness));

        Assert.Equal("Darkness", Room(harness, kael).Title);
    }

    [Fact]
    public void An_ordinary_item_is_not_a_light()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", West);
        Darken(harness, West);
        harness.Equip(kael, Lantern(harness, lights: false), ItemSlot.OffHand);

        Assert.Equal("Darkness", Room(harness, kael).Title);
    }

    /// <summary>
    /// The whole reason light belongs to the room rather than the viewer: one lantern between six.
    /// </summary>
    [Fact]
    public void One_persons_lantern_lights_the_room_for_everybody()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", West);
        var mira = harness.AddPlayer("Mira", West);
        Darken(harness, West);
        harness.Equip(mira, Lantern(harness), ItemSlot.OffHand);

        Assert.Equal("The west room", Room(harness, kael).Title);
    }

    /// <summary>The lamp leaves with the person carrying it.</summary>
    [Fact]
    public void The_room_goes_dark_again_when_the_light_walks_out()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", West);
        var mira = harness.AddPlayer("Mira", West);
        Darken(harness, West);
        harness.Equip(mira, Lantern(harness), ItemSlot.OffHand);
        Assert.Equal("The west room", Room(harness, kael).Title);

        harness.World.Move(mira, East);

        Assert.Equal("Darkness", Room(harness, kael).Title);
    }

    /// <summary>
    /// A lit item on the floor is not a light. It has to be picked up and worn, which is the
    /// difference between a light source and a lamp post.
    /// </summary>
    [Fact]
    public void A_light_source_lying_on_the_floor_does_nothing()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", West);
        Darken(harness, West);
        harness.DropItemInRoom(Lantern(harness), West);

        Assert.Equal("Darkness", Room(harness, kael).Title);
    }

    // -----------------------------------------------------------------------
    // The refresh path, which is a second renderer and used to be able to disagree
    // -----------------------------------------------------------------------

    [Fact]
    public void A_refresh_withholds_the_same_things_a_look_does()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", West);
        harness.AddPlayer("Mira", West);
        Darken(harness, West);
        harness.Drain(kael);

        harness.View.RefreshRoom(harness.World, West);
        var events = harness.Drain(kael);

        var contents = (ContentsPayload)events.Last(e => e.Type == EventTypes.Contents).Payload;
        var map = (MapPayload)events.Last(e => e.Type == EventTypes.Map).Payload;

        Assert.Empty(contents.Occupants);
        Assert.Empty(contents.Items);
        Assert.Empty(map.Entities);
    }

    /// <summary>
    /// Everybody in the room is still sent to. Emptying the occupant list is what is drawn, not who
    /// is drawn to — and getting that backwards would mean nobody in a dark room ever updates.
    /// </summary>
    [Fact]
    public void A_refresh_still_reaches_everyone_standing_in_the_dark()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", West);
        var mira = harness.AddPlayer("Mira", West);
        Darken(harness, West);
        harness.Drain(kael);
        harness.Drain(mira);

        harness.View.RefreshRoom(harness.World, West);

        Assert.Contains(harness.Drain(kael), e => e.Type == EventTypes.Map);
        Assert.Contains(harness.Drain(mira), e => e.Type == EventTypes.Map);
    }
}
