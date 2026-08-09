using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Mutations;
using DikuWeb.Engine.Tests.Infrastructure;

namespace DikuWeb.Engine.Tests.Mutations;

/// <summary>
/// Editing the §4.4 difficulty dial (PLAN.md §7.5).
/// </summary>
/// <remarks>
/// Multipliers were seed-and-SQL only: the primitives carried no multipliers at all, so a
/// builder's save reached the name and the flags and silently dropped the numbers the whole
/// scaling design is built around.
/// </remarks>
public sealed class MultiplierEditTests
{
    private static WorldHarness Loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return harness;
    }

    private static Multipliers Doubled() => new() { Xp = 2m, Strength = 2m };

    [Fact]
    public void Editing_a_zone_stores_its_multipliers()
    {
        var harness = Loaded();

        var result = harness.Mutate(new UpsertZone(
            "test.zone", "test", "Test Zone", "", 1, 50, new FlagSet(), Doubled()));

        Assert.True(result.Success);
        Assert.Equal(2m, harness.World.FindZone("test.zone")!.Multipliers.Xp);
        Assert.Equal(2m, harness.World.FindZone("test.zone")!.Multipliers.Strength);
    }

    [Fact]
    public void Editing_a_world_stores_its_multipliers()
    {
        var harness = Loaded();

        var result = harness.Mutate(new UpsertWorld(
            "test", "Test", "", 0, new FlagSet(), Doubled()));

        Assert.True(result.Success);
        Assert.Equal(2m, harness.World.FindWorld("test")!.Multipliers.Xp);
    }

    [Fact]
    public void A_new_zone_carries_the_multipliers_it_was_created_with()
    {
        var harness = Loaded();

        harness.Mutate(new UpsertZone(
            "test.deep", "test", "Deep", "", 1, 50, new FlagSet(), new Multipliers { Gold = 3m }));

        Assert.Equal(3m, harness.World.FindZone("test.deep")!.Multipliers.Gold);
    }

    /// <summary>
    /// The primitive is held for replay into the database, so the world must not share the
    /// instance the change carries — mutating one afterwards would silently rewrite the other.
    /// </summary>
    [Fact]
    public void The_stored_multipliers_are_a_copy_not_the_changes_own_instance()
    {
        var harness = Loaded();
        var carried = Doubled();

        harness.Mutate(new UpsertZone(
            "test.zone", "test", "Test Zone", "", 1, 50, new FlagSet(), carried));
        carried.Xp = 99m;

        Assert.Equal(2m, harness.World.FindZone("test.zone")!.Multipliers.Xp);
    }

    [Fact]
    public void The_edit_reaches_the_persistence_queue()
    {
        var harness = Loaded();

        harness.Editor.Apply(
            new UpsertZone("test.zone", "test", "Test Zone", "", 1, 50, new FlagSet(), Doubled()),
            Guid.NewGuid());

        var written = Assert.IsType<UpsertZone>(Assert.Single(harness.Writes.AllChanges));
        Assert.Equal(2m, written.Multipliers.Xp);
    }

    [Fact]
    public void An_unspecified_multiplier_stays_neutral()
    {
        var harness = Loaded();

        harness.Mutate(new UpsertZone(
            "test.zone", "test", "Test Zone", "", 1, 50, new FlagSet(), new Multipliers { Xp = 5m }));

        var stored = harness.World.FindZone("test.zone")!.Multipliers;
        Assert.Equal(5m, stored.Xp);
        Assert.Equal(1m, stored.Gold);
        Assert.Equal(1m, stored.Damage);
    }

    /// <summary>
    /// Multipliers resolve once, at spawn time (§4.4). A live edit therefore changes what spawns
    /// next, not what is already standing in the room - which is what the "Respawn zone" button
    /// is for, and worth pinning so nobody "fixes" it into a retroactive rewrite.
    /// </summary>
    [Fact]
    public void Existing_mobs_keep_the_numbers_they_spawned_with()
    {
        var harness = Loaded();
        var rat = harness.AddMob("rat", RoomKey.Parse("test.zone.west"));
        rat.ResolvedXp = 10;

        harness.Mutate(new UpsertZone(
            "test.zone", "test", "Test Zone", "", 1, 50, new FlagSet(), new Multipliers { Xp = 100m }));

        Assert.Equal(10, rat.ResolvedXp);
    }
}
