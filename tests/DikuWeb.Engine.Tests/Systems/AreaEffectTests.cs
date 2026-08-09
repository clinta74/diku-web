using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Mutations;
using DikuWeb.Engine.Tests.Infrastructure;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Tests.Systems;

/// <summary>
/// Area targeting — the last of the three targeting modes (PLAN.md §4.11).
/// </summary>
/// <remarks>
/// The rule that shapes every test here is <b>filter per target, never per room</b>. Asking once
/// whether the room permits the cast and then hitting everything standing in it would make a
/// single flag the difference between a spell and a massacre, and the room an AoE is cast in is
/// routinely mixed — mobs to kill, a shopkeeper behind them, and other players who must not be
/// touched.
///
/// Parties arrive in 5.3. Until then a helpful AoE reaches the room rather than the group, which
/// is generous in the safe direction, and a harmful one is held back from players by the
/// <c>pvp</c> flag rather than by party membership.
/// </remarks>
public sealed class AreaEffectTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");
    private static readonly RoomKey East = RoomKey.Parse("test.zone.east");

    /// <summary>Firestorm: 20 pulses of cast time, then it lands.</summary>
    private const int FirestormPulses = 21;

    /// <summary>Benediction: 16 pulses.</summary>
    private const int BenedictionPulses = 17;

    private static WorldHarness Loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return harness;
    }

    /// <summary>
    /// An Adept who can afford Firestorm. The harness does not recompute maxima when it sets a
    /// level, and the ability is the most expensive in the game on purpose.
    /// </summary>
    private static PlayerActor Adept(WorldHarness harness, string name = "Ilse", RoomKey? at = null)
    {
        var actor = harness.AddPlayer(name, at ?? West, path: CharacterPath.Adept, level: 18);
        actor.Character.Vitals.FocusMax = 200;
        actor.Character.Vitals.Focus = 200;
        harness.DefineAbility("adept.firestorm");
        return actor;
    }

    private static PlayerActor Hallow(WorldHarness harness, string name = "Bram", RoomKey? at = null)
    {
        var actor = harness.AddPlayer(name, at ?? West, path: CharacterPath.Hallow, level: 18);
        actor.Character.Vitals.FocusMax = 200;
        actor.Character.Vitals.Focus = 200;
        harness.DefineAbility("hallow.benediction");
        return actor;
    }

    private static Dictionary<string, object> Npc() =>
        WorldHarness.AsPersisted(new Dictionary<string, object> { ["type"] = "npc" });

    // -----------------------------------------------------------------------
    // What it gathers
    // -----------------------------------------------------------------------

    [Fact]
    public void A_hostile_area_ability_hits_every_mob_in_the_room()
    {
        var harness = Loaded();
        var caster = Adept(harness);

        var first = harness.AddMob("rat", West, health: 100);
        var second = harness.AddMob("wolf", West, health: 100, name: "wolf");
        var third = harness.AddMob("boar", West, health: 100, name: "boar");

        harness.Execute(caster, "cast firestorm");
        harness.Pump(FirestormPulses);

        Assert.True(first.Vitals.Health < 100);
        Assert.True(second.Vitals.Health < 100);
        Assert.True(third.Vitals.Health < 100);
    }

    [Fact]
    public void It_reaches_only_the_room_the_caster_is_standing_in()
    {
        // Combat is room-local (§4.2), and so is everything built on top of it.
        var harness = Loaded();
        var caster = Adept(harness);

        var here = harness.AddMob("rat", West, health: 100);
        var elsewhere = harness.AddMob("wolf", East, health: 100, name: "wolf");

        harness.Execute(caster, "cast firestorm");
        harness.Pump(FirestormPulses);

        Assert.True(here.Vitals.Health < 100);
        Assert.Equal(100, elsewhere.Vitals.Health);
    }

    [Fact]
    public void It_skips_a_non_combatant()
    {
        // An NPC is unattackable everywhere, so an area effect must not become the loophole that
        // kills the shopkeeper standing behind the wolves and strands whoever was mid-quest.
        var harness = Loaded();
        var caster = Adept(harness);

        var wolf = harness.AddMob("wolf", West, health: 100, name: "wolf");
        var keeper = harness.AddMob("keeper", West, health: 100, name: "keeper", behavior: Npc());

        harness.Execute(caster, "cast firestorm");
        harness.Pump(FirestormPulses);

        Assert.True(wolf.Vitals.Health < 100);
        Assert.Equal(100, keeper.Vitals.Health);
    }

    [Fact]
    public void It_never_touches_the_caster()
    {
        var harness = Loaded();
        var caster = Adept(harness);
        harness.AddMob("rat", West, health: 100);

        var before = caster.Character.Vitals.Health;
        harness.Execute(caster, "cast firestorm");
        harness.Pump(FirestormPulses);

        Assert.Equal(before, caster.Character.Vitals.Health);
    }

    // -----------------------------------------------------------------------
    // Per-target filtering — the point of §4.11
    // -----------------------------------------------------------------------

    [Fact]
    public void It_leaves_another_player_alone_in_an_ordinary_room()
    {
        var harness = Loaded();
        var caster = Adept(harness);
        var bystander = harness.AddPlayer("Kael", West);
        var rat = harness.AddMob("rat", West, health: 100);

        var before = bystander.Character.Vitals.Health;
        harness.Execute(caster, "cast firestorm");
        harness.Pump(FirestormPulses);

        // The mixed room: the mobs burn, the person standing next to them does not.
        Assert.True(rat.Vitals.Health < 100);
        Assert.Equal(before, bystander.Character.Vitals.Health);
    }

    [Fact]
    public void It_reaches_another_player_where_the_room_allows_it()
    {
        var harness = Loaded();
        harness.Mutate(new SetRoomFlag(West, RoomFlags.Pvp.Key, true));

        var caster = Adept(harness);
        var rival = harness.AddPlayer("Kael", West);

        var before = rival.Character.Vitals.Health;
        harness.Execute(caster, "cast firestorm");
        harness.Pump(FirestormPulses);

        Assert.True(rival.Character.Vitals.Health < before);
    }

    [Fact]
    public void A_pvp_flag_one_scope_up_is_enough()
    {
        // Flags resolve nearest-level-wins (§4.10), so a duelling zone does not have to be
        // flagged room by room for an area effect to honour it.
        var harness = Loaded();
        harness.Mutate(new SetZoneFlag("test.zone", RoomFlags.Pvp.Key, true));

        var caster = Adept(harness);
        var rival = harness.AddPlayer("Kael", West);

        var before = rival.Character.Vitals.Health;
        harness.Execute(caster, "cast firestorm");
        harness.Pump(FirestormPulses);

        Assert.True(rival.Character.Vitals.Health < before);
    }

    [Fact]
    public void Nothing_lands_in_a_peaceful_room()
    {
        // Peaceful beats everything, the same way it does for a swing.
        var harness = Loaded();
        harness.Mutate(new SetRoomFlag(West, RoomFlags.Peaceful.Key, true));

        var caster = Adept(harness);
        var rat = harness.AddMob("rat", West, health: 100);

        harness.Execute(caster, "cast firestorm");
        harness.Pump(FirestormPulses);

        Assert.Equal(100, rat.Vitals.Health);
    }

    // -----------------------------------------------------------------------
    // Cost
    // -----------------------------------------------------------------------

    [Fact]
    public void An_area_ability_that_gathers_nobody_costs_nothing()
    {
        // The same rule single-target casts already follow: everything that can fail is settled
        // before anything is spent, so a cast that lands on nothing is not paid for.
        var harness = Loaded();
        var caster = Adept(harness);

        harness.Execute(caster, "cast firestorm");
        harness.Pump(FirestormPulses);

        Assert.Equal(200, caster.Character.Vitals.Focus);
        Assert.Null(harness.World.GetAbilityCooldown(caster.CharacterId, "adept.firestorm"));
    }

    [Fact]
    public void A_peaceful_room_costs_nothing_either()
    {
        var harness = Loaded();
        harness.Mutate(new SetRoomFlag(West, RoomFlags.Peaceful.Key, true));

        var caster = Adept(harness);
        harness.AddMob("rat", West, health: 100);

        harness.Execute(caster, "cast firestorm");
        harness.Pump(FirestormPulses);

        Assert.Equal(200, caster.Character.Vitals.Focus);
    }

    [Fact]
    public void One_cast_pays_one_cost_however_many_it_lands_on()
    {
        // This is the whole reason to bring an AoE, and the reason its cost and cooldown are the
        // steepest in the game: paying per target would make it a slower single-target spell.
        var harness = Loaded();
        var caster = Adept(harness);

        harness.AddMob("rat", West, health: 100);
        harness.AddMob("wolf", West, health: 100, name: "wolf");
        harness.AddMob("boar", West, health: 100, name: "boar");
        harness.AddMob("crow", West, health: 100, name: "crow");

        harness.Execute(caster, "cast firestorm");
        harness.Pump(FirestormPulses);

        Assert.Equal(200 - 60, caster.Character.Vitals.Focus);
    }

    // -----------------------------------------------------------------------
    // The helpful direction
    // -----------------------------------------------------------------------

    [Fact]
    public void A_helpful_area_ability_reaches_the_caster_and_the_people_with_them()
    {
        var harness = Loaded();
        var caster = Hallow(harness);
        var friend = harness.AddPlayer("Kael", West);

        caster.Character.Vitals.Health = 10;
        friend.Character.Vitals.Health = 10;

        harness.Execute(caster, "cast benediction");
        harness.Pump(BenedictionPulses);

        Assert.True(caster.Character.Vitals.Health > 10);
        Assert.True(friend.Character.Vitals.Health > 10);
    }

    [Fact]
    public void A_helpful_area_ability_leaves_the_mobs_alone()
    {
        // The filter has two directions precisely so a room heal does not mend the wolves.
        var harness = Loaded();
        var caster = Hallow(harness);
        var wolf = harness.AddMob("wolf", West, health: 100, name: "wolf");
        wolf.Vitals.Health = 10;

        harness.Execute(caster, "cast benediction");
        harness.Pump(BenedictionPulses);

        Assert.Equal(10, wolf.Vitals.Health);
    }

    [Fact]
    public void A_helpful_area_ability_works_in_a_peaceful_room()
    {
        // Peaceful forbids combat, not healing. Gating both on the one flag would make a safe
        // room the one place a support Path cannot do its job.
        var harness = Loaded();
        harness.Mutate(new SetRoomFlag(West, RoomFlags.Peaceful.Key, true));

        var caster = Hallow(harness);
        caster.Character.Vitals.Health = 10;

        harness.Execute(caster, "cast benediction");
        harness.Pump(BenedictionPulses);

        Assert.True(caster.Character.Vitals.Health > 10);
    }

    // -----------------------------------------------------------------------
    // Which way an ability points
    // -----------------------------------------------------------------------

    [Fact]
    public void A_bare_cast_of_a_wound_does_not_land_on_the_caster()
    {
        // Harmfulness used to be a hardcoded list of two effect keys in the command layer, so
        // every executor written after it read as helpful: `cast scorch` with no target named
        // resolved to the caster and set them on fire.
        var harness = Loaded();
        var caster = harness.AddPlayer("Ilse", West, path: CharacterPath.Adept, level: 18);
        caster.Character.Vitals.FocusMax = 200;
        caster.Character.Vitals.Focus = 200;
        harness.DefineAbility("adept.scorch");

        var before = caster.Character.Vitals.Health;

        harness.Execute(caster, "cast scorch");
        harness.Pump(10);

        Assert.Equal(before, caster.Character.Vitals.Health);
        Assert.Empty(harness.World.GetActiveEffects(caster.CharacterId));
    }

    [Fact]
    public void A_bare_cast_of_a_heal_still_lands_on_the_caster()
    {
        var harness = Loaded();
        var caster = harness.AddPlayer("Bram", West, path: CharacterPath.Hallow, level: 10);
        caster.Character.Vitals.FocusMax = 200;
        caster.Character.Vitals.Focus = 200;
        harness.DefineAbility("hallow.mend");
        caster.Character.Vitals.Health = 5;

        harness.Execute(caster, "cast mend");
        harness.Pump(6);

        Assert.True(caster.Character.Vitals.Health > 5);
    }
}
