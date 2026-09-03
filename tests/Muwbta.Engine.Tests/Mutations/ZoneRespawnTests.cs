using Muwbta.Domain.Entities;
using Muwbta.Domain.Inhabitants;
using Muwbta.Domain.Spawning;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Mutations;
using Muwbta.Engine.Tests.Infrastructure;

namespace Muwbta.Engine.Tests.Mutations;

/// <summary>
/// The <em>Respawn zone</em> button (PLAN.md §7.5).
/// </summary>
/// <remarks>
/// Multipliers resolve once, at spawn time (§4.4), so a difficulty edit reaches the next spawn and
/// never the mob already standing in the room. That is the design and <c>MultiplierEditTests</c>
/// pins it; this is the other half, and until it existed a builder's only ways to see the numbers
/// they had just typed were to clear the zone by hand or to restart the server.
/// </remarks>
public sealed class ZoneRespawnTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");

    private static WorldHarness Loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return harness;
    }

    /// <summary>Registers a mob template and a spawner that fills one room with it.</summary>
    private static Spawner Spawning(
        WorldHarness harness,
        int target = 2,
        string zoneKey = "test.zone",
        int baseHealth = 40)
    {
        harness.MobTemplates.Put(new MobTemplate
        {
            Key = "rat",
            Name = "a rat",
            Icon = "r",
            Level = 3,
            BaseStats = new Dictionary<string, object> { ["health"] = baseHealth },
        });

        var spawner = new Spawner
        {
            Id = Guid.CreateVersion7(),
            ZoneKey = zoneKey,
            TemplateKey = "rat",
            TemplateKind = TemplateKind.Mob,
            RoomKeys = [West.ToString()],
            TargetCount = target,
            RespawnSeconds = 3600,
        };

        harness.Spawners.Put(spawner);
        return spawner;
    }

    private static RespawnTally Respawn(WorldHarness harness, string zoneKey = "test.zone")
    {
        var result = harness.Mutate(new RespawnZone(zoneKey));
        Assert.True(result.Success, result.Message);
        return result.Respawned!;
    }

    [Fact]
    public void An_empty_zone_is_filled_to_its_spawners_targets()
    {
        var harness = Loaded();
        Spawning(harness, target: 2);

        var tally = Respawn(harness);

        Assert.Equal(0, tally.Despawned);
        Assert.Equal(2, tally.Spawned);
        Assert.Equal(2, harness.World.MobsIn(West).Count);
    }

    /// <summary>
    /// The reason the button exists: what is standing there afterwards carries the numbers the
    /// builder just saved, not the ones it was born with.
    /// </summary>
    [Fact]
    public void The_mobs_that_come_back_carry_the_zones_current_multipliers()
    {
        var harness = Loaded();
        Spawning(harness, target: 1, baseHealth: 40);

        Respawn(harness);
        Assert.Equal(40, harness.World.MobsIn(West).Single().Vitals.HealthMax);

        harness.Mutate(new UpsertZone(
            "test.zone", "test", "Test Zone", "", 1, 50, new FlagSet(),
            new Multipliers { Health = 3m }));

        var tally = Respawn(harness);

        Assert.Equal(1, tally.Despawned);
        Assert.Equal(1, tally.Spawned);
        Assert.Equal(120, harness.World.MobsIn(West).Single().Vitals.HealthMax);
    }

    /// <summary>
    /// A spawner still waiting out its <c>respawnSeconds</c> is filled the whole way, which is
    /// why the tally has two numbers rather than one.
    /// </summary>
    [Fact]
    public void A_zone_below_its_target_comes_back_at_full_population()
    {
        var harness = Loaded();
        Spawning(harness, target: 3);

        Respawn(harness);
        harness.World.RemoveMob(harness.World.MobsIn(West)[0]);

        var tally = Respawn(harness);

        Assert.Equal(2, tally.Despawned);
        Assert.Equal(3, tally.Spawned);
        Assert.Equal(3, harness.World.MobsIn(West).Count);
    }

    /// <summary>
    /// Nothing puts a hand-placed mob back, so despawning one would be a delete wearing a
    /// refresh's name.
    /// </summary>
    [Fact]
    public void A_mob_nobody_spawned_is_left_standing()
    {
        var harness = Loaded();
        Spawning(harness, target: 1);
        var conjured = harness.AddMob("badger", West);

        Respawn(harness);

        Assert.Contains(harness.World.MobsIn(West), m => m.Id == conjured.Id);
    }

    /// <summary>
    /// Ownership is the spawner, not the room: a neighbour's mob carries a neighbour's dials.
    /// </summary>
    [Fact]
    public void A_mob_from_another_zones_spawner_is_left_alone()
    {
        var harness = Loaded();
        Spawning(harness, target: 1);
        Respawn(harness);
        var elsewhere = harness.World.MobsIn(West).Single();

        harness.Mutate(new UpsertZone(
            "test.other", "test", "Other", "", 1, 50, new FlagSet(), new Multipliers()));
        var tally = Respawn(harness, "test.other");

        Assert.Equal(0, tally.Despawned);
        Assert.Equal(0, tally.Spawned);
        Assert.Contains(harness.World.MobsIn(West), m => m.Id == elsewhere.Id);
    }

    /// <summary>
    /// A combatant that vanishes from the world but not from the fight leaves whoever was
    /// swinging at it stuck in a fight with nothing to hit - the same cleanup the in-game
    /// <c>despawn</c> verb does, for the same reason.
    /// </summary>
    [Fact]
    public void A_mob_taken_out_mid_fight_leaves_the_fight_too()
    {
        var harness = Loaded();
        Spawning(harness, target: 1);
        Respawn(harness);

        var rat = harness.World.MobsIn(West).Single();
        var player = harness.AddPlayer("Aldis", West);
        var combat = harness.World.GetOrCreateCombat(West);
        combat.AddCombatant(EntityId.ForCharacter(player.CharacterId));
        combat.AddCombatant(EntityId.ForMob(rat.Id));

        Respawn(harness);

        Assert.DoesNotContain(EntityId.ForMob(rat.Id), combat.Combatants);
    }

    /// <summary>
    /// An item on the floor may be one somebody is about to pick up, and §7.5 says this applies
    /// to living mobs.
    /// </summary>
    [Fact]
    public void An_item_spawners_placements_are_left_where_they_lie()
    {
        var harness = Loaded();
        Spawning(harness, target: 1);

        harness.Spawners.Put(new Spawner
        {
            Id = Guid.CreateVersion7(),
            ZoneKey = "test.zone",
            TemplateKey = "torch",
            TemplateKind = TemplateKind.Item,
            RoomKeys = [West.ToString()],
            TargetCount = 1,
        });

        var torch = harness.DropItemInRoom(
            harness.DefineItem("torch", "a torch", slot: null), West);

        Respawn(harness);

        Assert.Contains(harness.World.ItemsIn(West), i => i.Id == torch.Id);
    }

    /// <summary>
    /// Mobs live in memory alone, so there is no row to write and no audit entry to make. A
    /// primitive escaping into the write queue here would be a content edit nobody made.
    /// </summary>
    [Fact]
    public void Nothing_reaches_the_persistence_queue()
    {
        var harness = Loaded();
        Spawning(harness, target: 2);

        harness.Editor.Apply(new RespawnZone("test.zone"), Guid.NewGuid());

        Assert.Empty(harness.Writes.AllChanges);
    }

    [Fact]
    public void A_zone_that_is_not_there_is_refused()
    {
        var harness = Loaded();

        var result = harness.Mutate(new RespawnZone("test.missing"));

        Assert.False(result.Success);
        Assert.Equal(MutationError.NotFound, result.Error);
    }

    /// <summary>
    /// Everything else in the applier degrades when a cache is absent, because the edit itself
    /// still lands. Here the caches are the operation, and a respawn that cleared a zone and then
    /// found nothing to refill from would be the most expensive possible way to fail quietly.
    /// </summary>
    [Fact]
    public void An_applier_without_the_caches_refuses_rather_than_emptying_the_zone()
    {
        var harness = Loaded();
        Spawning(harness, target: 1);
        Respawn(harness);

        var bare = new WorldMutationApplier(harness.World, harness.View, harness.Options);
        var result = bare.Apply(new RespawnZone("test.zone"));

        Assert.False(result.Success);
        Assert.Equal(MutationError.Invalid, result.Error);
        Assert.Single(harness.World.MobsIn(West));
    }
}
