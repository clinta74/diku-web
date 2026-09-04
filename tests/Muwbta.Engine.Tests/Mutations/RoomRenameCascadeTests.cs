using Muwbta.Domain.Inhabitants;
using Muwbta.Domain.Spawning;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Inhabitants;
using Muwbta.Engine.Mutations;
using Muwbta.Engine.Spawning;
using Muwbta.Engine.Tests.Infrastructure;

namespace Muwbta.Engine.Tests.Mutations;

/// <summary>
/// A room key is named in more places than its exits, and a rename has to follow all of them
/// (PLAN.md §7.6).
/// </summary>
/// <remarks>
/// Every one of these failures is silent, which is why they are worth pinning. A spawner left
/// pointing at the old key looks exactly like one that is already satisfied; a mob left behind is
/// in a room the world no longer has, so the AI looks the room up, finds nothing, and returns -
/// the mob never moves or fights again while still counting against its spawner's population.
/// </remarks>
public sealed class RoomRenameCascadeTests
{
    private static readonly RoomKey Middle = RoomKey.Parse("test.zone.middle");
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");
    private static readonly RoomKey Renamed = RoomKey.Parse("test.zone.the-crossroads");

    private static WorldHarness Loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return harness;
    }

    private static Guid AddSpawner(WorldHarness harness, params string[] rooms) =>
        AddSpawner(harness, fightsAtLevel: null, rooms);

    private static Guid AddSpawner(WorldHarness harness, int? fightsAtLevel, params string[] rooms)
    {
        var id = Guid.CreateVersion7();

        harness.Mutate(new UpsertSpawner(
            id, "test.zone", "rat", TemplateKind.Mob, [.. rooms], 2, RespawnSeconds: 60, Wanders: null, fightsAtLevel,
            // Every field is carried through a rename or quietly reset; the assertion below is the one that would notice.
            NameModifier: "deep"));

        return id;
    }

    private static Mob AddRat(WorldHarness harness, RoomKey at)
    {
        var template = new MobTemplate { Key = "rat", Name = "a rat", Icon = "r", Level = 1 };
        var mob = new MobSpawner().Spawn(template, harness.Zone, harness.World_, at);

        harness.World.AddMob(mob);
        return mob;
    }

    [Fact]
    public void A_spawner_follows_the_room_it_fills()
    {
        var harness = Loaded();
        var id = AddSpawner(harness, Middle.ToString());

        harness.Mutate(new RenameRoom(Middle, Renamed));

        Assert.Equal([Renamed.ToString()], harness.Spawners.Get(id)!.RoomKeys);
    }

    [Fact]
    public void The_spawner_edit_reaches_persistence_as_well_as_the_cache()
    {
        // The cache alone would put the rule back on the old key at the next restart, which is
        // the same bug with a delay on it.
        var harness = Loaded();
        var id = AddSpawner(harness, Middle.ToString(), West.ToString());

        var result = harness.Mutate(new RenameRoom(Middle, Renamed));

        var written = result.Applied.OfType<UpsertSpawner>().Single(s => s.Id == id);
        Assert.Equal([Renamed.ToString(), West.ToString()], written.RoomKeys);
    }

    [Fact]
    public void A_spawner_that_already_lists_the_new_key_does_not_get_it_twice()
    {
        // A spawner may already list the key being renamed onto - the builder is free to have
        // listed a room that does not exist yet. A duplicate entry would silently double that
        // room's odds of being picked.
        var harness = Loaded();
        var id = AddSpawner(harness, Middle.ToString(), Renamed.ToString());

        harness.Mutate(new RenameRoom(Middle, Renamed));

        Assert.Equal([Renamed.ToString()], harness.Spawners.Get(id)!.RoomKeys);
    }

    [Fact]
    public void Spawners_pointing_elsewhere_are_left_alone()
    {
        var harness = Loaded();
        var id = AddSpawner(harness, West.ToString());

        var result = harness.Mutate(new RenameRoom(Middle, Renamed));

        Assert.Equal([West.ToString()], harness.Spawners.Get(id)!.RoomKeys);
        Assert.DoesNotContain(result.Applied.OfType<UpsertSpawner>(), s => s.Id == id);
    }

    [Fact]
    public void The_mobs_standing_there_come_with_it()
    {
        var harness = Loaded();
        var rat = AddRat(harness, Middle);

        harness.Mutate(new RenameRoom(Middle, Renamed));

        Assert.Equal(Renamed.ToString(), rat.RoomKey);
        Assert.Contains(rat, harness.World.MobsIn(Renamed));
        Assert.Empty(harness.World.MobsIn(Middle));
    }

    [Fact]
    public void A_mob_keeps_a_home_zone_it_can_actually_wander_in()
    {
        // The home zone was recorded at spawn and bounds where the mob may go. A rename that
        // also changes the zone would otherwise fence it out of the zone it is standing in -
        // every exit fails the border check, so it could never move again.
        var harness = Loaded();
        harness.Mutate(new UpsertZone(
            "test.elsewhere", "test", "Elsewhere", "", 1, 50, new FlagSet(), new Multipliers()));

        var rat = AddRat(harness, Middle);
        var moved = RoomKey.Parse("test.elsewhere.middle");

        harness.Mutate(new RenameRoom(Middle, moved));

        Assert.Equal("test.elsewhere", MobState.HomeZoneOf(rat));
    }

    [Fact]
    public void A_roaming_mob_that_was_only_passing_through_keeps_its_own_home()
    {
        // Only the mobs whose home is the renamed room's zone are moved with it. One that
        // wandered in from next door still belongs next door.
        var harness = Loaded();
        harness.Mutate(new UpsertZone(
            "test.elsewhere", "test", "Elsewhere", "", 1, 50, new FlagSet(), new Multipliers()));

        var rat = AddRat(harness, Middle);
        rat.State[MobState.HomeZoneKey] = "test.faraway";

        harness.Mutate(new RenameRoom(Middle, RoomKey.Parse("test.elsewhere.middle")));

        Assert.Equal("test.faraway", MobState.HomeZoneOf(rat));
    }

    [Fact]
    public void The_items_on_the_floor_come_with_it()
    {
        var harness = Loaded();
        var template = harness.DefineItem("temper", "a temper", slot: null);
        var temper = new ItemSpawner().Spawn(template, harness.Zone, harness.World_, Middle);
        harness.World.AddItem(temper);

        harness.Mutate(new RenameRoom(Middle, Renamed));

        Assert.Equal(Renamed.ToString(), temper.RoomKey);
        Assert.Contains(temper, harness.World.ItemsIn(Renamed));
        Assert.Empty(harness.World.ItemsIn(Middle));
    }

    [Fact]
    public void A_moved_item_is_handed_off_to_be_saved()
    {
        // Ground items are persisted, so an in-memory move alone is undone by the next restart -
        // the row would still name the room that was renamed.
        var harness = Loaded();
        var template = harness.DefineItem("temper", "a temper", slot: null);
        var temper = new ItemSpawner().Spawn(template, harness.Zone, harness.World_, Middle);
        harness.World.AddItem(temper);

        harness.Mutate(new RenameRoom(Middle, Renamed));

        Assert.Contains(temper, harness.ItemSaves.Saved);
    }

    [Fact]
    public void A_renamed_room_does_not_cost_the_spawner_its_other_settings()
    {
        // RepointSpawners rebuilds the whole spawner to change one list, so every field it forgets
        // is silently reset - and the reset happens a long way from the edit that caused it, on a
        // room rename nobody connects to a mob's difficulty.
        //
        // Asserted on the two overrides rather than on the room list, because the room list is what
        // the method is *for* and would be noticed immediately.
        var harness = Loaded();
        var id = AddSpawner(harness, fightsAtLevel: 27, Middle.ToString());

        harness.Mutate(new RenameRoom(Middle, Renamed));

        var spawner = Assert.Single(harness.Spawners.All, s => s.Id == id);

        Assert.Equal(27, spawner.FightsAtLevel);
        Assert.Equal("deep", spawner.NameModifier);
        Assert.Equal(2, spawner.TargetCount);
        Assert.Contains(Renamed.ToString(), spawner.RoomKeys);
    }
}
