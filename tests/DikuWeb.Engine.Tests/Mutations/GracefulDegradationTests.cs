using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Mutations;
using DikuWeb.Engine.Tests.Infrastructure;

namespace DikuWeb.Engine.Tests.Mutations;

/// <summary>
/// One test per row of PLAN.md §7.4. Live editing has no draft/publish gate, so the world is
/// <em>allowed</em> to be invalid and the loop must never throw because of it - a mutation that
/// took down the loop would take the world down for every connected player.
/// </summary>
public sealed class GracefulDegradationTests
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
    public void An_exit_pointing_at_a_nonexistent_room_fails_closed_for_a_player()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", East);

        harness.Execute(kael, "north");

        Assert.Contains("The way is blocked", harness.DrainText(kael), StringComparison.Ordinal);
        Assert.Equal(East, kael.RoomKey);
    }

    [Fact]
    public void The_same_exit_offers_a_builder_the_dig_that_would_fix_it()
    {
        var harness = Loaded();
        var mira = harness.AddPlayer("Mira", East, Domain.Accounts.AccountRole.Builder);

        harness.Execute(mira, "north");

        var text = harness.DrainText(mira);
        Assert.Contains("dig north", text, StringComparison.Ordinal);
        Assert.DoesNotContain("The way is blocked", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Deleting_a_room_moves_its_occupants_somewhere_real()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Middle);

        var result = harness.Mutate(new DeleteRoom(Middle));

        Assert.True(result.Success);
        Assert.NotEqual(Middle, kael.RoomKey);
        Assert.NotNull(harness.World.FindRoom(kael.RoomKey));
        Assert.Contains(kael, harness.World.OccupantsOf(kael.RoomKey));
        Assert.Empty(harness.World.OccupantsOf(Middle));
    }

    [Fact]
    public void Deleting_the_last_room_in_a_zone_still_lands_the_occupant_somewhere()
    {
        // The refuge chain has to survive its own preferred answer being gone: there is no
        // sibling room to fall back to here.
        var harness = new WorldHarness();
        var only = WorldHarness.NewRoom("only");
        harness.World.Load(
            [new Domain.Worlds.World { Key = "test", Name = "Test" }],
            [new Zone { Key = "test.zone", WorldKey = "test", Name = "Test Zone" }],
            [only]);

        var kael = harness.AddPlayer("Kael", only.Key);

        var result = harness.Mutate(new DeleteRoom(only.Key));

        Assert.True(result.Success);
        Assert.Null(harness.World.FindRoom(only.Key));

        // Nowhere left to go, so the character keeps a key that does not resolve rather than
        // the loop throwing. PlayerView narrates that state instead of crashing.
        Assert.Empty(harness.World.OccupantsOf(only.Key));
    }

    [Fact]
    public void Deleting_a_room_leaves_inbound_exits_dangling_rather_than_rewriting_neighbours()
    {
        var harness = Loaded();

        harness.Mutate(new DeleteRoom(Middle));

        var west = harness.World.FindRoom(West)!;
        var exit = west.ExitTo(Direction.East);

        Assert.NotNull(exit);
        Assert.Equal(Middle, exit.ToRoomKey);
        Assert.Null(harness.World.FindRoom(exit.ToRoomKey));
    }

    [Fact]
    public void A_zone_with_players_in_it_cannot_be_deleted()
    {
        var harness = Loaded();
        harness.AddPlayer("Kael", West);

        var result = harness.Mutate(new DeleteZone("test.zone"));

        Assert.False(result.Success);
        Assert.Equal(MutationError.Occupied, result.Error);
        Assert.Contains("Kael", result.Message, StringComparison.Ordinal);
        Assert.NotNull(harness.World.FindZone("test.zone"));
        Assert.Empty(result.Applied);
    }

    [Fact]
    public void An_empty_zone_can_be_deleted_and_takes_its_rooms_with_it()
    {
        var harness = Loaded();

        var result = harness.Mutate(new DeleteZone("test.zone"));

        Assert.True(result.Success);
        Assert.Null(harness.World.FindZone("test.zone"));
        Assert.Null(harness.World.FindRoom(West));
    }

    [Fact]
    public void A_room_with_no_description_still_works()
    {
        var harness = Loaded();
        var room = harness.World.FindRoom(West)!;

        var result = harness.Mutate(
            WorldMutationApplier.ToUpsert(room) with { Description = string.Empty });

        Assert.True(result.Success);

        var kael = harness.AddPlayer("Kael", West);
        harness.Execute(kael, "look");

        Assert.Contains("The west room", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_flag_key_is_refused_rather_than_stored()
    {
        var harness = Loaded();

        var result = harness.Mutate(new SetRoomFlag(West, "notARealFlag", true));

        Assert.False(result.Success);
        Assert.Equal(MutationError.Invalid, result.Error);
        Assert.False(harness.World.FindRoom(West)!.Flags.Has("notARealFlag"));
    }

    [Fact]
    public void A_wrong_typed_flag_value_resolves_to_the_safe_default()
    {
        var harness = Loaded();
        var room = harness.World.FindRoom(West)!;
        room.Flags.Set(RoomFlags.Pvp.Key, FlagValue.Of("yes"));

        Assert.False(harness.World.IsFlagSet(West, RoomFlags.Pvp));
    }

    [Fact]
    public void A_flag_on_a_room_that_does_not_exist_resolves_to_the_default()
    {
        var harness = Loaded();

        Assert.False(harness.World.IsFlagSet(Nowhere, RoomFlags.Pvp));
    }

    [Fact]
    public void Editing_a_room_pushes_it_to_everyone_standing_in_it()
    {
        // PLAN.md §3.5: live edits reach occupants without them relogging.
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", West);
        harness.Drain(kael);

        var room = harness.World.FindRoom(West)!;
        harness.Mutate(WorldMutationApplier.ToUpsert(room) with { Title = "The Repainted Room" });

        Assert.Contains("The Repainted Room", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void Refused_mutations_produce_no_writes()
    {
        // The invariant that keeps a rejected edit out of the database entirely.
        var harness = Loaded();
        harness.AddPlayer("Kael", West);

        WorldChange[] doomed =
        [
            new DeleteZone("test.zone"),
            new DeleteRoom(Nowhere),
            new SetRoomFlag(Nowhere, RoomFlags.Pvp.Key, true),
            new UnlinkExit(West, Direction.Up),
            new RenameRoom(Nowhere, RoomKey.Parse("test.zone.elsewhere")),
            new UpsertZone("other.zone", "other", "Other", "", 1, 50, new FlagSet(), new Multipliers()),
        ];

        foreach (var change in doomed)
        {
            var result = harness.Editor.Apply(change, accountId: null);
            Assert.False(result.Success);
            Assert.Empty(result.Applied);
        }

        Assert.Empty(harness.Writes.Jobs);
    }

    [Fact]
    public void Nothing_in_the_applier_throws_on_garbage_input()
    {
        // The applier's contract is that it refuses, never throws - the loop catches as a
        // backstop, but reaching that catch is a bug rather than a refusal.
        var harness = Loaded();

        WorldChange[] garbage =
        [
            new DeleteWorld("no-such-world"),
            new UpsertRoom(
                RoomKey.Parse("ghost.zone.room"), "ghost.zone", "T", "D",
                new FlagSet(), [], new Dictionary<string, string>(), null, null),
            new LinkExit(Nowhere, Direction.North, West),
            new DigRoom(Nowhere, Direction.North),
            new SetExit(Nowhere, Direction.North, Nowhere),
        ];

        foreach (var change in garbage)
        {
            var result = harness.Applier.Apply(change);
            Assert.False(result.Success);
        }
    }
}
