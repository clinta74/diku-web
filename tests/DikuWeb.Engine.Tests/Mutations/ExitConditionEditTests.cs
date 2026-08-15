using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Mutations;
using DikuWeb.Engine.Tests.Infrastructure;

namespace DikuWeb.Engine.Tests.Mutations;

/// <summary>
/// Editing an exit that has conditions on it (PLAN.md §4.15).
/// </summary>
/// <remarks>
/// <b>Every test here is about a lock disappearing when nobody asked it to.</b> A gate that refuses
/// when it should not is a bug report within the hour; a gate that quietly stopped refusing is
/// found by a level 4 standing in the last realm. So the edits that touch an exit for other reasons
/// - repointing it, renaming the room it leaves - are pinned here, and so is the one edit that is
/// genuinely allowed to remove a lock, because a lock with no way off is its own kind of broken.
/// </remarks>
public sealed class ExitConditionEditTests
{
    private const string Flag = "attuned.grask";

    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");
    private static readonly RoomKey Middle = RoomKey.Parse("test.zone.middle");
    private static readonly RoomKey East = RoomKey.Parse("test.zone.east");

    private static WorldHarness Gated()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.Mutate(new SetExit(East, Direction.West, West, Flag, "brass-key", "It is locked."));
        return harness;
    }

    private static RoomExit? ExitOf(WorldHarness harness, RoomKey room, Direction direction) =>
        harness.World.FindRoom(room)?.ExitTo(direction);

    [Fact]
    public void SetExit_stores_all_three_conditions()
    {
        var harness = Gated();

        var exit = ExitOf(harness, East, Direction.West);

        Assert.NotNull(exit);
        Assert.Equal(Flag, exit.RequiredFlagKey);
        Assert.Equal("brass-key", exit.RequiredItemKey);
        Assert.Equal("It is locked.", exit.RefusalMessage);
        Assert.True(exit.IsConditional);
    }

    [Fact]
    public void SetExit_with_nulls_takes_the_lock_off()
    {
        // The one edit that is meant to. A PUT of a whole exit states what it is, so an omitted
        // condition is a condition it does not have - otherwise a lock could never be removed.
        var harness = Gated();

        harness.Mutate(new SetExit(East, Direction.West, West));

        var exit = ExitOf(harness, East, Direction.West);
        Assert.NotNull(exit);
        Assert.Null(exit.RequiredFlagKey);
        Assert.Null(exit.RequiredItemKey);
        Assert.False(exit.IsConditional);
    }

    [Fact]
    public void Repointing_with_link_leaves_the_conditions_alone()
    {
        // `link` says where a door goes and nothing about who may use it. A builder fixing the far
        // end of a locked exit must not discover they have unlocked it.
        var harness = Gated();

        harness.Mutate(new LinkExit(East, Direction.West, Middle, Reciprocal: false));

        var exit = ExitOf(harness, East, Direction.West);
        Assert.NotNull(exit);
        Assert.Equal(Middle, exit.ToRoomKey);
        Assert.Equal(Flag, exit.RequiredFlagKey);
        Assert.Equal("brass-key", exit.RequiredItemKey);
    }

    [Fact]
    public void Renaming_the_room_carries_the_lock_across()
    {
        // The rename rebuilds every exit leaving the room. Rebuilt from the request rather than
        // from the exits themselves, it would rebuild them unlocked.
        var harness = Gated();
        var renamed = RoomKey.Parse("test.zone.gatehouse");

        var result = harness.Mutate(new RenameRoom(East, renamed));
        Assert.True(result.Success);

        var exit = ExitOf(harness, renamed, Direction.West);
        Assert.NotNull(exit);
        Assert.Equal(Flag, exit.RequiredFlagKey);
        Assert.Equal("brass-key", exit.RequiredItemKey);
        Assert.Equal("It is locked.", exit.RefusalMessage);
    }

    [Fact]
    public void Renaming_the_destination_carries_an_inbound_lock_across()
    {
        // The other half of a rename: exits pointing *at* the room are repointed too, and they
        // have conditions of their own.
        var harness = Gated();
        var renamed = RoomKey.Parse("test.zone.vault");

        var result = harness.Mutate(new RenameRoom(West, renamed));
        Assert.True(result.Success);

        var exit = ExitOf(harness, East, Direction.West);
        Assert.NotNull(exit);
        Assert.Equal(renamed, exit.ToRoomKey);
        Assert.Equal(Flag, exit.RequiredFlagKey);
    }

    [Fact]
    public void A_malformed_flag_key_is_refused_rather_than_stored()
    {
        // Shape is all that can be checked - which flags are real is a property of the authored
        // world, and /validate answers that. But a key with a space in it can never be granted by
        // anything, so there is no reason to let it into the database.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var before = ExitOf(harness, East, Direction.West);
        Assert.NotNull(before);

        var result = harness.Mutate(new SetExit(East, Direction.West, West, "Attuned To Grask"));

        Assert.False(result.Success);

        // Refused whole: the exit is not repointed either. A mutation that failed validation must
        // leave nothing behind, or a rejected save is indistinguishable from a partial one.
        var after = ExitOf(harness, East, Direction.West);
        Assert.NotNull(after);
        Assert.Equal(Middle, after.ToRoomKey);
        Assert.False(after.IsConditional);
    }

    [Fact]
    public void A_reciprocal_link_does_not_mirror_the_lock_by_default()
    {
        // You can always leave a vault. Digging and linking are two-way because a corridor you
        // cannot walk back down is rarely meant; a lock is one-way for the opposite reason.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        harness.Mutate(new LinkExit(
            East,
            Direction.West,
            West,
            Reciprocal: true,
            ApplyConditions: true,
            RequiredFlagKey: Flag));

        Assert.Equal(Flag, ExitOf(harness, East, Direction.West)?.RequiredFlagKey);
        Assert.Null(ExitOf(harness, West, Direction.East)?.RequiredFlagKey);
    }

    [Fact]
    public void Asking_for_it_mirrors_the_lock_both_ways()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        harness.Mutate(new LinkExit(
            East,
            Direction.West,
            West,
            Reciprocal: true,
            ApplyConditions: true,
            RequiredFlagKey: Flag,
            ReciprocalConditions: true));

        Assert.Equal(Flag, ExitOf(harness, East, Direction.West)?.RequiredFlagKey);
        Assert.Equal(Flag, ExitOf(harness, West, Direction.East)?.RequiredFlagKey);
    }
}
