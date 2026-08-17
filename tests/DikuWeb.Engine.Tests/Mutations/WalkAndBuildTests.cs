using DikuWeb.Domain.Accounts;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Mutations;
using DikuWeb.Engine.Tests.Infrastructure;

namespace DikuWeb.Engine.Tests.Mutations;

/// <summary>
/// Walk-and-build, PLAN.md §7.6. The point of the feature is that the exit graph comes out
/// correct by construction, so most of these assert the graph rather than the response.
/// </summary>
public sealed class WalkAndBuildTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");
    private static readonly RoomKey Middle = RoomKey.Parse("test.zone.middle");
    private static readonly RoomKey East = RoomKey.Parse("test.zone.east");
    private static readonly RoomKey Nowhere = RoomKey.Parse("test.zone.nowhere");

    private static WorldHarness Loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return harness;
    }

    [Fact]
    public void Digging_creates_the_room_and_the_exit_to_reach_it()
    {
        var harness = Loaded();

        var result = harness.Mutate(new DigRoom(West, Direction.North));

        Assert.True(result.Success);
        var created = result.AffectedRoom!.Value;

        Assert.NotNull(harness.World.FindRoom(created));
        Assert.Equal(created, harness.World.FindRoom(West)!.ExitTo(Direction.North)!.ToRoomKey);
    }

    [Fact]
    public void Digging_links_the_new_room_back_by_default()
    {
        var harness = Loaded();

        var created = harness.Mutate(new DigRoom(West, Direction.North)).AffectedRoom!.Value;
        var back = harness.World.FindRoom(created)!.ExitTo(Direction.South);

        Assert.NotNull(back);
        Assert.Equal(West, back.ToRoomKey);
    }

    [Fact]
    public void A_one_way_passage_has_to_be_asked_for()
    {
        // Two-way is right often enough that the inverse should be the deliberate act.
        var harness = Loaded();

        var created = harness
            .Mutate(new DigRoom(West, Direction.North, Reciprocal: false))
            .AffectedRoom!.Value;

        Assert.Null(harness.World.FindRoom(created)!.ExitTo(Direction.South));
    }

    [Fact]
    public void Digging_into_a_dangling_exit_materializes_the_room_the_exit_already_names()
    {
        // The materialize case: east already has a north exit pointing at test.zone.nowhere,
        // which does not exist. Digging must reuse that key so the existing link resolves,
        // rather than creating a second room and leaving the first link broken.
        var harness = Loaded();

        var result = harness.Mutate(new DigRoom(East, Direction.North));

        Assert.True(result.Success);
        Assert.Equal(Nowhere, result.AffectedRoom);
        Assert.NotNull(harness.World.FindRoom(Nowhere));
        Assert.Equal(Nowhere, harness.World.FindRoom(East)!.ExitTo(Direction.North)!.ToRoomKey);
    }

    [Fact]
    public void Materializing_does_not_produce_a_duplicate_exit_write()
    {
        // The exit already exists, so only the room is new. Writing the exit again would be
        // harmless but would put a misleading row in content_audit.
        var harness = Loaded();

        var result = harness.Mutate(new DigRoom(East, Direction.North, Reciprocal: false));

        Assert.Single(result.Applied);
        Assert.IsType<UpsertRoom>(result.Applied[0]);
    }

    [Fact]
    public void Digging_where_a_room_already_exists_is_refused()
    {
        var harness = Loaded();

        var result = harness.Mutate(new DigRoom(West, Direction.East));

        Assert.False(result.Success);
        Assert.Equal(MutationError.Conflict, result.Error);
    }

    [Fact]
    public void Generated_keys_take_the_lowest_free_number()
    {
        var harness = Loaded();

        var first = harness.Mutate(new DigRoom(West, Direction.North)).AffectedRoom!.Value;
        var second = harness.Mutate(new DigRoom(West, Direction.South)).AffectedRoom!.Value;

        Assert.Equal("room-1", first.Room);
        Assert.Equal("room-2", second.Room);
    }

    [Fact]
    public void New_rooms_are_born_unfinished()
    {
        var harness = Loaded();

        var created = harness.Mutate(new DigRoom(West, Direction.North)).AffectedRoom!.Value;
        var room = harness.World.FindRoom(created)!;

        Assert.True(room.Flags.BooleanOrNull(RoomFlags.Unfinished.Key));
        Assert.Empty(room.Grid);
        Assert.Contains("Unfinished", room.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void Canvas_placement_offsets_one_step_in_the_dug_direction()
    {
        var harness = Loaded();
        var west = harness.World.FindRoom(West)!;
        west.EditorX = 4;
        west.EditorY = 7;

        var north = harness.World.FindRoom(
            harness.Mutate(new DigRoom(West, Direction.North)).AffectedRoom!.Value)!;
        var south = harness.World.FindRoom(
            harness.Mutate(new DigRoom(West, Direction.South)).AffectedRoom!.Value)!;

        Assert.Equal((4, 6), (north.EditorX, north.EditorY));
        Assert.Equal((4, 8), (south.EditorX, south.EditorY));
    }

    [Fact]
    public void Up_and_down_reuse_the_source_cell()
    {
        // The canvas is 2D. A vertical passage marks a level rather than moving the box.
        var harness = Loaded();
        var west = harness.World.FindRoom(West)!;
        west.EditorX = 2;
        west.EditorY = 2;

        var below = harness.World.FindRoom(
            harness.Mutate(new DigRoom(West, Direction.Down)).AffectedRoom!.Value)!;

        Assert.Equal((2, 2), (below.EditorX, below.EditorY));
    }

    [Fact]
    public void Digging_never_silently_crosses_a_zone_boundary()
    {
        var harness = Loaded();

        var created = harness.Mutate(new DigRoom(West, Direction.North)).AffectedRoom!.Value;

        Assert.Equal("test.zone", created.ZoneKey);
    }

    [Fact]
    public void Digging_into_a_zone_that_does_not_exist_is_refused()
    {
        var harness = Loaded();

        var result = harness.Mutate(new DigRoom(West, Direction.North, ZoneKey: "test.missing"));

        Assert.False(result.Success);
        Assert.Equal(MutationError.NotFound, result.Error);
    }

    // -----------------------------------------------------------------------
    // Rename
    // -----------------------------------------------------------------------

    [Fact]
    public void Renaming_rewrites_inbound_exits_in_the_same_mutation()
    {
        // Otherwise renaming a dug room silently orphans its neighbours.
        var harness = Loaded();
        var target = RoomKey.Parse("test.zone.the-crossroads");

        var result = harness.Mutate(new RenameRoom(Middle, target));

        Assert.True(result.Success);
        Assert.Equal(target, harness.World.FindRoom(West)!.ExitTo(Direction.East)!.ToRoomKey);
        Assert.Equal(target, harness.World.FindRoom(East)!.ExitTo(Direction.West)!.ToRoomKey);
        Assert.Null(harness.World.FindRoom(Middle));
    }

    [Fact]
    public void Renaming_carries_the_rooms_own_exits_across()
    {
        var harness = Loaded();
        var target = RoomKey.Parse("test.zone.the-crossroads");

        harness.Mutate(new RenameRoom(Middle, target));
        var renamed = harness.World.FindRoom(target)!;

        Assert.Equal(West, renamed.ExitTo(Direction.West)!.ToRoomKey);
        Assert.Equal(East, renamed.ExitTo(Direction.East)!.ToRoomKey);
    }

    [Fact]
    public void Renaming_takes_occupants_with_it()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Middle);
        var target = RoomKey.Parse("test.zone.the-crossroads");

        harness.Mutate(new RenameRoom(Middle, target));

        Assert.Equal(target, kael.RoomKey);
        Assert.Contains(kael, harness.World.OccupantsOf(target));
    }

    [Fact]
    public void Renaming_onto_an_existing_key_is_refused()
    {
        var harness = Loaded();

        var result = harness.Mutate(new RenameRoom(Middle, East));

        Assert.False(result.Success);
        Assert.Equal(MutationError.Conflict, result.Error);
        Assert.NotNull(harness.World.FindRoom(Middle));
    }

    // -----------------------------------------------------------------------
    // In-game commands
    // -----------------------------------------------------------------------

    [Fact]
    public void A_player_cannot_dig()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", West);

        harness.Execute(kael, "dig north");

        // Worded as an unknown verb: a player has no business learning these exist.
        Assert.Contains("not something you can do", harness.DrainText(kael), StringComparison.Ordinal);
        Assert.Null(harness.World.FindRoom(West)!.ExitTo(Direction.North));
        Assert.Empty(harness.Writes.Jobs);
    }

    [Fact]
    public void A_builder_can_dig_from_the_command_line_and_it_queues_a_write()
    {
        var harness = Loaded();
        var mira = harness.AddPlayer("Mira", West, AccountRole.Builder);

        harness.Execute(mira, "dig north");

        Assert.NotNull(harness.World.FindRoom(West)!.ExitTo(Direction.North));
        Assert.NotEmpty(harness.Writes.Jobs);
        Assert.Contains(harness.Writes.AllChanges, c => c is UpsertRoom);
        Assert.Contains(harness.Writes.AllChanges, c => c is SetExit);
    }

    [Fact]
    public void Rflag_sets_a_flag_and_rflag_clear_restores_inheritance()
    {
        var harness = Loaded();
        var mira = harness.AddPlayer("Mira", West, AccountRole.Builder);
        harness.Zone.Flags.Set(RoomFlags.Pvp.Key, true);

        harness.Execute(mira, "rflag pvp off");
        Assert.False(harness.World.IsFlagSet(West, RoomFlags.Pvp));

        harness.Execute(mira, "rflag pvp clear");
        Assert.True(harness.World.IsFlagSet(West, RoomFlags.Pvp));
    }

    /// <summary>
    /// A word that is not on, off or clear is refused rather than read as "on".
    /// </summary>
    /// <remarks>
    /// The fallthrough was `_ => true`, so `rflag pvp of` — one keystroke short of "off" — turned
    /// PvP on and reported that it had. A careful three-state design undone for a typo
    /// (BUGS.md #25).
    /// </remarks>
    [Theory]
    [InlineData("of")]
    [InlineData("0")]
    [InlineData("maybe")]
    public void Rflag_refuses_a_value_it_does_not_understand(string value)
    {
        var harness = Loaded();
        var mira = harness.AddPlayer("Mira", West, AccountRole.Builder);
        harness.Drain(mira);

        harness.Execute(mira, $"rflag pvp {value}");

        Assert.False(harness.World.IsFlagSet(West, RoomFlags.Pvp));
        Assert.Contains("is not on, off, or clear", harness.DrainText(mira), StringComparison.Ordinal);
    }

    /// <summary>Bare `rflag <name>` still means on, which is the useful shorthand.</summary>
    [Fact]
    public void Rflag_with_no_value_still_turns_it_on()
    {
        var harness = Loaded();
        var mira = harness.AddPlayer("Mira", West, AccountRole.Builder);

        harness.Execute(mira, "rflag pvp");

        Assert.True(harness.World.IsFlagSet(West, RoomFlags.Pvp));
    }

    [Fact]
    public void Bare_rflag_says_where_each_value_came_from()
    {
        var harness = Loaded();
        var mira = harness.AddPlayer("Mira", West, AccountRole.Builder);
        harness.Zone.Flags.Set(RoomFlags.Pvp.Key, true);
        harness.Drain(mira);

        harness.Execute(mira, "rflag");

        var text = harness.DrainText(mira);
        Assert.Contains("pvp", text, StringComparison.Ordinal);
        Assert.Contains("(from zone)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Goto_moves_a_builder_without_needing_an_exit()
    {
        var harness = Loaded();
        var mira = harness.AddPlayer("Mira", West, AccountRole.Builder);

        harness.Execute(mira, "goto test.zone.east");

        Assert.Equal(East, mira.RoomKey);
        Assert.Contains(mira, harness.World.OccupantsOf(East));
    }

    [Fact]
    public void Goto_refuses_a_room_that_does_not_exist()
    {
        var harness = Loaded();
        var mira = harness.AddPlayer("Mira", West, AccountRole.Builder);

        harness.Execute(mira, "goto test.zone.nowhere");

        Assert.Equal(West, mira.RoomKey);
        Assert.Contains("no room", harness.DrainText(mira), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unlink_removes_both_halves_of_a_two_way_passage()
    {
        var harness = Loaded();
        var mira = harness.AddPlayer("Mira", West, AccountRole.Builder);

        harness.Execute(mira, "unlink east");

        Assert.Null(harness.World.FindRoom(West)!.ExitTo(Direction.East));
        Assert.Null(harness.World.FindRoom(Middle)!.ExitTo(Direction.West));
    }

    [Fact]
    public void Unlink_leaves_a_neighbours_unrelated_exit_alone()
    {
        // Middle's west exit is repointed at east, so it is a different passage that merely
        // happens to face this way. Removing it would be an edit nobody asked for.
        var harness = Loaded();
        harness.World.FindRoom(Middle)!.ExitTo(Direction.West)!.ToRoomKey = East;

        var result = harness.Mutate(new UnlinkExit(West, Direction.East));

        Assert.True(result.Success);
        Assert.NotNull(harness.World.FindRoom(Middle)!.ExitTo(Direction.West));
        Assert.Single(result.Applied);
    }

    [Fact]
    public void Rtitle_clears_nothing_it_should_not_and_keeps_the_grid()
    {
        var harness = Loaded();
        var mira = harness.AddPlayer("Mira", West, AccountRole.Builder);
        var before = harness.World.FindRoom(West)!.Grid.Count;

        harness.Execute(mira, "rtitle The Weeping Arch");

        var room = harness.World.FindRoom(West)!;
        Assert.Equal("The Weeping Arch", room.Title);
        Assert.Equal(before, room.Grid.Count);
    }

    [Fact]
    public void Help_hides_builder_verbs_from_players_and_shows_them_to_builders()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", West);
        var mira = harness.AddPlayer("Mira", West, AccountRole.Builder);

        harness.Execute(kael, "help");
        harness.Execute(mira, "help");

        Assert.DoesNotContain("dig <dir>", harness.DrainText(kael), StringComparison.Ordinal);
        Assert.Contains("dig <dir>", harness.DrainText(mira), StringComparison.Ordinal);
    }

    [Fact]
    public void Direction_abbreviations_still_beat_the_new_builder_verbs()
    {
        // "d" must stay "down" and "u" must stay "up" forever, whatever gets added later.
        var harness = Loaded();

        Assert.Equal("down", harness.Commands.Find("d")!.Name);
        Assert.Equal("up", harness.Commands.Find("u")!.Name);
        Assert.Equal("north", harness.Commands.Find("n")!.Name);
        Assert.Equal("look", harness.Commands.Find("l")!.Name);
    }
}
