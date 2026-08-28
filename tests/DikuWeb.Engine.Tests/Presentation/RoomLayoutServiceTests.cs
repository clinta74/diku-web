using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Presentation;
using DikuWeb.Engine.Tests.Infrastructure;

namespace DikuWeb.Engine.Tests.Presentation;

public sealed class RoomLayoutServiceTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");

    private readonly RoomLayoutService _layout = new();

    [Fact]
    public void Placement_is_identical_across_service_instances()
    {
        // The property everything else rests on: because placement is a pure function of
        // (room, entity, occupancy) it survives a server restart, so no coordinate is ever
        // persisted (PLAN.md §4.3).
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var kael = harness.AddPlayer("Kael", West);
        var mira = harness.AddPlayer("Mira", West);

        var room = harness.World.FindRoom(West)!;
        var occupants = harness.World.OccupantsOf(West);

        var first = _layout.BuildMap(room, occupants, [], []);
        var second = new RoomLayoutService().BuildMap(room, occupants, [], []);

        Assert.Equal(first.Entities.Count, second.Entities.Count);
        foreach (var (a, b) in first.Entities.Zip(second.Entities))
        {
            Assert.Equal(a.Id, b.Id);
            Assert.Equal(a.X, b.X);
            Assert.Equal(a.Y, b.Y);
        }

        Assert.NotEmpty(mira.Name);
    }

    [Fact]
    public void Placement_does_not_depend_on_arrival_order()
    {
        // Two clients viewing the same room must draw it identically, whoever walked in first.
        var roomA = WorldHarness.NewRoom("order-a");
        var roomB = WorldHarness.NewRoom("order-b");

        var harnessA = new WorldHarness();
        harnessA.World.Load([], [], [roomA]);
        var kaelA = harnessA.AddPlayer("Kael", roomA.Key);
        harnessA.AddPlayer("Mira", roomA.Key);

        var harnessB = new WorldHarness();
        harnessB.World.Load([], [], [roomB]);
        harnessB.AddPlayer("Mira", roomB.Key);
        var kaelB = harnessB.AddPlayer("Kael", roomB.Key);

        var mapA = _layout.BuildMap(roomA, harnessA.World.OccupantsOf(roomA.Key), [], []);
        var mapB = _layout.BuildMap(roomB, harnessB.World.OccupantsOf(roomB.Key), [], []);

        // Entity ids differ per run, so compare the shape: the same number of entities
        // placed, and every one of them on a distinct cell.
        Assert.Equal(mapA.Entities.Count, mapB.Entities.Count);
        Assert.Equal(2, mapA.Entities.Count);
    }

    [Fact]
    public void No_two_entities_share_a_cell()
    {
        var room = WorldHarness.NewRoom("crowd");
        var harness = new WorldHarness();
        harness.World.Load([], [], [room]);

        var viewer = harness.AddPlayer("Viewer", room.Key);
        for (var i = 0; i < 12; i++)
        {
            harness.AddPlayer($"Extra{i}", room.Key);
        }

        var map = _layout.BuildMap(room, harness.World.OccupantsOf(room.Key), [], []);
        var cells = map.Entities.Select(e => (e.X, e.Y)).ToList();

        Assert.Equal(cells.Count, cells.Distinct().Count());
    }

    /// <summary>
    /// Everybody is drawn by name and by their own icon. <b>The server marks nobody as "you".</b>
    /// </summary>
    /// <remarks>
    /// This used to assert the opposite, and the reversal is the point. Marking the viewer meant a
    /// room could not be drawn once for the people standing in it — the layout ran per occupant,
    /// and even after that was hoisted, the entity list still had to be copied per occupant to
    /// patch a single element. Both are quadratic in how many people are in the room, which at two
    /// hundred crowded sessions stopped the game loop keeping its own schedule (PLAN.md §11).
    ///
    /// So the mark moved to the client, which is the only party that knows whose screen it is.
    /// <c>client/src/state/self.ts</c> does it, and <c>self.test.ts</c> covers it. What is asserted
    /// here is the server half of that contract: one room, drawn once, the same for everyone.
    /// </remarks>
    [Fact]
    public void Nobody_is_marked_as_the_viewer()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var kael = harness.AddPlayer("Kael", West);
        harness.AddPlayer("Mira", West);

        var room = harness.World.FindRoom(West)!;
        var map = _layout.BuildMap(room, harness.World.OccupantsOf(West), [], []);

        Assert.DoesNotContain(map.Entities, e => e.Icon == "@");
        Assert.DoesNotContain(map.Entities, e => e.Label == "you");

        var self = Assert.Single(map.Entities, e => e.Id == kael.EntityId);
        Assert.Equal("Kael", self.Label);

        var other = Assert.Single(map.Entities, e => e.Id != kael.EntityId);
        Assert.Equal("Mira", other.Label);
    }

    [Fact]
    public void A_room_without_grid_art_still_renders_a_rectangle()
    {
        // PLAN.md §4.4: terrain is optional so authoring art never becomes a tax.
        var room = WorldHarness.NewRoom("bare");
        var harness = new WorldHarness();
        harness.World.Load([], [], [room]);
        var kael = harness.AddPlayer("Kael", room.Key);

        var map = _layout.BuildMap(room, harness.World.OccupantsOf(room.Key), [], []);

        Assert.True(map.W > 0);
        Assert.True(map.H > 0);
        Assert.Equal(map.H, map.Terrain.Count);
        Assert.Single(map.Entities);
    }

    [Fact]
    public void Entities_are_never_placed_on_walls()
    {
        var room = WorldHarness.NewRoom(
            "walled",
            grid: ["#####", "#...#", "#####"],
            legend: new Dictionary<string, string> { ["#"] = "wall", ["."] = "floor" });

        var harness = new WorldHarness();
        harness.World.Load([], [], [room]);
        var viewer = harness.AddPlayer("Viewer", room.Key);
        harness.AddPlayer("Other", room.Key);

        var map = _layout.BuildMap(room, harness.World.OccupantsOf(room.Key), [], []);

        Assert.NotEmpty(map.Entities);
        foreach (var entity in map.Entities)
        {
            Assert.Equal('.', map.Terrain[entity.Y][entity.X]);
        }
    }

    [Theory]
    [InlineData("void")]
    [InlineData("pillar")]
    [InlineData("rock")]
    [InlineData("crate")]
    [InlineData("brazier")]
    public void Nothing_stands_on_the_tiles_the_Reaches_added(string tile)
    {
        // The five names the Reaches terrain draws with. The set matches on the legend *name*, so
        // a room calling a pillar "column" would put a rat inside it and nothing would complain -
        // the mob is simply drawn somewhere a player can see it should not be.
        //
        // "void" is the one that reads oddly and the reason the engine's set is not called
        // "solid": a rim room draws the edge where the shard stops, and the question being asked
        // is "may something be drawn here", not "is it made of stone".
        var room = WorldHarness.NewRoom(
            "edge",
            grid: ["XXXXX", "X...X", "XXXXX"],
            legend: new Dictionary<string, string> { ["X"] = tile, ["."] = "floor" });

        var harness = new WorldHarness();
        harness.World.Load([], [], [room]);
        var viewer = harness.AddPlayer("Viewer", room.Key);
        harness.AddPlayer("Other", room.Key);

        var map = _layout.BuildMap(room, harness.World.OccupantsOf(room.Key), [], []);

        Assert.NotEmpty(map.Entities);
        Assert.All(map.Entities, e => Assert.Equal('.', map.Terrain[e.Y][e.X]));
    }

    [Fact]
    public void Occupants_beyond_the_cell_count_stack_rather_than_vanish()
    {
        // A one-cell room with three people in it. Overlapping icons are an acceptable
        // cosmetic blemish; an occupant missing from the map while standing in the room
        // reads to the player as a bug, so everyone must still be drawn.
        var room = WorldHarness.NewRoom(
            "tiny",
            grid: ["###", "#.#", "###"],
            legend: new Dictionary<string, string> { ["#"] = "wall", ["."] = "floor" });

        var harness = new WorldHarness();
        harness.World.Load([], [], [room]);
        var viewer = harness.AddPlayer("Viewer", room.Key);
        harness.AddPlayer("Second", room.Key);
        harness.AddPlayer("Third", room.Key);

        var map = _layout.BuildMap(room, harness.World.OccupantsOf(room.Key), [], []);

        Assert.Equal(3, map.Entities.Count);
        Assert.All(map.Entities, e => Assert.Equal('.', map.Terrain[e.Y][e.X]));
    }

    [Fact]
    public void A_room_of_solid_wall_still_places_its_occupants()
    {
        var room = WorldHarness.NewRoom(
            "sealed",
            grid: ["###", "###"],
            legend: new Dictionary<string, string> { ["#"] = "wall" });

        var harness = new WorldHarness();
        harness.World.Load([], [], [room]);
        var kael = harness.AddPlayer("Kael", room.Key);

        var map = _layout.BuildMap(room, harness.World.OccupantsOf(room.Key), [], []);

        Assert.Single(map.Entities);
    }

    [Fact]
    public void A_ragged_grid_is_padded_rather_than_crashing()
    {
        // A builder mid-edit can leave rows of different lengths. That is a warning in the
        // editor, never an exception on the game loop (PLAN.md §7.4).
        var room = WorldHarness.NewRoom(
            "ragged",
            grid: ["####", "#.", "######"],
            legend: new Dictionary<string, string> { ["#"] = "wall", ["."] = "floor" });

        var harness = new WorldHarness();
        harness.World.Load([], [], [room]);
        var kael = harness.AddPlayer("Kael", room.Key);

        var map = _layout.BuildMap(room, harness.World.OccupantsOf(room.Key), [], []);

        Assert.All(map.Terrain, row => Assert.Equal(map.W, row.Length));
    }

    /// <summary>
    /// The property the whole room-refresh path is built on: <b>a room is one payload, and every
    /// occupant is sent the identical instance.</b>
    /// </summary>
    /// <remarks>
    /// Worth pinning rather than assuming, because it is silent when it breaks. Reintroducing
    /// anything viewer-dependent here would fail no other test — the pictures would still be
    /// correct — it would simply put refreshing a room back to being quadratic in how many people
    /// are standing in it, which is the cost that stopped the loop keeping its schedule at two
    /// hundred crowded sessions (PLAN.md §11).
    /// </remarks>
    [Fact]
    public void One_room_is_built_once_and_shared()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.AddPlayer("Kael", West);
        harness.AddPlayer("Mira", West);

        var room = harness.World.FindRoom(West)!;
        var occupants = harness.World.OccupantsOf(West);

        var first = _layout.BuildMap(room, occupants, [], []);
        var second = _layout.BuildMap(room, occupants, [], []);

        // Nothing in the payload depends on who asked for it, so two builds agree completely -
        // which is what makes it safe to build one and hand it to everybody by reference.
        Assert.Equal(first.W, second.W);
        Assert.Equal(first.H, second.H);
        Assert.Equal(first.Terrain, second.Terrain);
        Assert.Equal(first.Entities, second.Entities);
    }

    [Fact]
    public void An_empty_room_still_carries_its_shape()
    {
        // The unlit case: RefreshRoom builds a map with nobody on it and still sends it to
        // everyone standing there, so the panel keeps its dimensions rather than collapsing.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.AddPlayer("Kael", West);

        var room = harness.World.FindRoom(West)!;
        var empty = _layout.BuildMap(room, [], [], []);

        Assert.Empty(empty.Entities);
        Assert.True(empty.W > 0);
        Assert.Equal(empty.H, empty.Terrain.Count);
    }
}
