using Muwbta.Domain.Worlds;
using Muwbta.Engine.Mutations;
using Muwbta.Engine.Tests.Infrastructure;

namespace Muwbta.Engine.Tests.Mutations;

/// <summary>
/// Editing one flag at a time on a world or a zone (PLAN.md §4.10).
/// </summary>
/// <remarks>
/// Rooms have had a single-flag primitive since flags existed; the two scopes above them did a
/// read-modify-write of the whole map instead. That is the shape that loses a concurrent edit
/// silently - the second request carries a complete, valid map that happens to be stale, so
/// nothing anywhere reports a problem and a flag simply reverts.
///
/// The blast radius argues for the narrow primitive most strongly at the top: a lost room edit
/// costs one room, a lost world edit costs every room under it.
/// </remarks>
public sealed class ScopedFlagEditTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");

    private static WorldHarness Loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return harness;
    }

    // -----------------------------------------------------------------------
    // The point of the primitive
    // -----------------------------------------------------------------------

    [Fact]
    public void Setting_one_zone_flag_leaves_its_siblings_alone()
    {
        var harness = Loaded();
        harness.Mutate(new SetZoneFlag("test.zone", RoomFlags.Dark.Key, true));

        // The concurrent builder's edit. Under the old whole-map PATCH this second write would
        // have carried a map without `dark` and quietly undone the first one.
        harness.Mutate(new SetZoneFlag("test.zone", RoomFlags.NoRecall.Key, true));

        var flags = harness.World.FindZone("test.zone")!.Flags;
        Assert.True(flags.Has(RoomFlags.Dark.Key));
        Assert.True(flags.Has(RoomFlags.NoRecall.Key));
    }

    [Fact]
    public void Setting_one_world_flag_leaves_its_siblings_alone()
    {
        var harness = Loaded();
        harness.Mutate(new SetWorldFlag("test", RoomFlags.Pvp.Key, true));
        harness.Mutate(new SetWorldFlag("test", RoomFlags.Peaceful.Key, false));

        var flags = harness.World.FindWorld("test")!.Flags;
        Assert.True(flags.Has(RoomFlags.Pvp.Key));
        Assert.True(flags.Has(RoomFlags.Peaceful.Key));
    }

    [Fact]
    public void A_flag_edit_does_not_disturb_the_difficulty_dial()
    {
        // The applied primitive is a whole-row upsert, so everything it carries has to be read
        // back off the entity rather than defaulted - otherwise toggling `dark` would silently
        // reset the zone's multipliers to neutral on the next save.
        var harness = Loaded();
        harness.Mutate(new UpsertZone(
            "test.zone", "test", "Test Zone", "", 3, 9, new FlagSet(), new Multipliers { Xp = 2m }));

        var result = harness.Mutate(new SetZoneFlag("test.zone", RoomFlags.Dark.Key, true));

        var applied = Assert.IsType<UpsertZone>(Assert.Single(result.Applied));
        Assert.Equal(2m, applied.Multipliers.Xp);
        Assert.Equal("Test Zone", applied.Name);
        Assert.Equal(3, applied.MinLevel);
        Assert.Equal(9, applied.MaxLevel);
        Assert.True(applied.Flags.Has(RoomFlags.Dark.Key));
    }

    [Fact]
    public void A_world_flag_edit_carries_the_world_forward_intact()
    {
        var harness = Loaded();
        harness.Mutate(new UpsertWorld("test", "Test", "A place.", 7, new FlagSet(), new Multipliers { Gold = 3m }));

        var result = harness.Mutate(new SetWorldFlag("test", RoomFlags.Pvp.Key, true));

        var applied = Assert.IsType<UpsertWorld>(Assert.Single(result.Applied));
        Assert.Equal("A place.", applied.Description);
        Assert.Equal(7, applied.SortOrder);
        Assert.Equal(3m, applied.Multipliers.Gold);
    }

    // -----------------------------------------------------------------------
    // Three states, resolved down the chain
    // -----------------------------------------------------------------------

    [Fact]
    public void A_world_flag_reaches_a_room_that_declares_nothing()
    {
        var harness = Loaded();

        harness.Mutate(new SetWorldFlag("test", RoomFlags.Pvp.Key, true));

        Assert.True(harness.World.IsFlagSet(West, RoomFlags.Pvp));
    }

    [Fact]
    public void A_zone_flag_beats_the_world_below_it()
    {
        var harness = Loaded();
        harness.Mutate(new SetWorldFlag("test", RoomFlags.Pvp.Key, true));

        // "off" is a decision about this zone, not an absence - a duelling world with one safe
        // zone in it is the case this exists for.
        harness.Mutate(new SetZoneFlag("test.zone", RoomFlags.Pvp.Key, false));

        Assert.False(harness.World.IsFlagSet(West, RoomFlags.Pvp));
    }

    [Fact]
    public void Clearing_a_zone_flag_hands_the_decision_back_to_the_world()
    {
        var harness = Loaded();
        harness.Mutate(new SetWorldFlag("test", RoomFlags.Pvp.Key, true));
        harness.Mutate(new SetZoneFlag("test.zone", RoomFlags.Pvp.Key, false));

        // The third state. A null clears the key rather than storing false, which is why the
        // world's `true` comes back rather than the zone's `false` sticking.
        var result = harness.Mutate(new SetZoneFlag("test.zone", RoomFlags.Pvp.Key, null));

        Assert.True(result.Success);
        Assert.False(harness.World.FindZone("test.zone")!.Flags.Has(RoomFlags.Pvp.Key));
        Assert.True(harness.World.IsFlagSet(West, RoomFlags.Pvp));
    }

    [Fact]
    public void Clearing_a_world_flag_falls_back_to_the_registry_default()
    {
        var harness = Loaded();
        harness.Mutate(new SetWorldFlag("test", RoomFlags.Pvp.Key, true));

        harness.Mutate(new SetWorldFlag("test", RoomFlags.Pvp.Key, null));

        // Absence is the safe value: no flag anywhere means no PvP (§4.10).
        Assert.False(harness.World.IsFlagSet(West, RoomFlags.Pvp));
    }

    // -----------------------------------------------------------------------
    // Refusals
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("notARealFlag")]
    [InlineData("")]
    public void An_unknown_flag_key_is_refused_at_both_scopes(string flag)
    {
        var harness = Loaded();

        var zone = harness.Mutate(new SetZoneFlag("test.zone", flag, true));
        var world = harness.Mutate(new SetWorldFlag("test", flag, true));

        Assert.Equal(MutationError.Invalid, zone.Error);
        Assert.Equal(MutationError.Invalid, world.Error);
        Assert.False(harness.World.FindZone("test.zone")!.Flags.Has(flag));
        Assert.False(harness.World.FindWorld("test")!.Flags.Has(flag));
    }

    [Fact]
    public void A_flag_on_something_that_does_not_exist_is_refused()
    {
        var harness = Loaded();

        Assert.Equal(
            MutationError.NotFound,
            harness.Mutate(new SetZoneFlag("test.nowhere", RoomFlags.Dark.Key, true)).Error);
        Assert.Equal(
            MutationError.NotFound,
            harness.Mutate(new SetWorldFlag("nowhere", RoomFlags.Dark.Key, true)).Error);
    }

    [Fact]
    public void A_refused_flag_edit_produces_no_writes()
    {
        var harness = Loaded();

        WorldChange[] doomed =
        [
            new SetZoneFlag("test.nowhere", RoomFlags.Dark.Key, true),
            new SetWorldFlag("nowhere", RoomFlags.Dark.Key, true),
            new SetZoneFlag("test.zone", "notARealFlag", true),
            new SetWorldFlag("test", "notARealFlag", true),
        ];

        foreach (var change in doomed)
        {
            var result = harness.Editor.Apply(change, accountId: null);
            Assert.False(result.Success);
            Assert.Empty(result.Applied);
        }

        Assert.Empty(harness.Writes.Jobs);
    }

    // -----------------------------------------------------------------------
    // Live
    // -----------------------------------------------------------------------

    [Fact]
    public void A_zone_flag_edit_refreshes_the_people_standing_in_it()
    {
        // Live edits reach players without a relog (PLAN.md §3.5). A zone flag can change what
        // the room does - `peaceful` stops a fight - so the room event must be resent.
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", West);
        harness.DrainText(kael);

        harness.Mutate(new SetZoneFlag("test.zone", RoomFlags.Dark.Key, true));

        // Darkening the zone is the strongest form of this: the resent room does not merely arrive,
        // it arrives changed. Kael is carrying no light, so the room he was reading a moment ago
        // has stopped saying its own name.
        var text = harness.DrainText(kael);
        Assert.Contains("Darkness", text, StringComparison.Ordinal);
        Assert.DoesNotContain("The west room", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_world_flag_edit_refreshes_the_people_standing_in_it()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", West);
        harness.DrainText(kael);

        harness.Mutate(new SetWorldFlag("test", RoomFlags.Pvp.Key, true));

        Assert.Contains("The west room", harness.DrainText(kael), StringComparison.Ordinal);
    }
}
