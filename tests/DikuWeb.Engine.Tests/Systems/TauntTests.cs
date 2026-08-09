using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Entities;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Mutations;
using DikuWeb.Engine.Tests.Infrastructure;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Tests.Systems;

/// <summary>
/// Taunt — the first thing in the game that writes threat instead of earning it.
/// </summary>
/// <remarks>
/// Every other route to the top of a hate list runs through dealing more damage than anyone else,
/// which is the rule that makes threat legible. Taunt is the deliberate exception, and it exists
/// because the alternative is a party in which the only safe damage is damage small enough not to
/// pull the mob — a tax on the Adept for doing the thing the Adept is for.
///
/// <b>What it buys is a lead, not a lock.</b> The list is still cumulative damage afterwards, so
/// whoever was displaced climbs back by out-damaging the taunter from there. A taunt that pinned
/// a mob outright would delete the decision it exists to create.
/// </remarks>
public sealed class TauntTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");

    private static WorldHarness Loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return harness;
    }

    private static PlayerActor Warden(WorldHarness harness, string name = "Theron")
    {
        var actor = harness.AddPlayer(name, West, path: CharacterPath.Warden, level: 10);
        actor.Character.Vitals.StaminaMax = 500;
        actor.Character.Vitals.Stamina = 500;
        harness.DefineAbility("warden.taunt");
        return actor;
    }

    private static string MobId(Mob mob) => EntityId.ForMob(mob.Id);

    private static string PlayerId(PlayerActor actor) => EntityId.ForCharacter(actor.CharacterId);

    private static int HateFor(WorldHarness harness, Mob mob, PlayerActor actor) =>
        harness.World.FindCombat(West)?.HateOf(MobId(mob), PlayerId(actor)) ?? 0;

    private static string? TopHater(WorldHarness harness, Mob mob) =>
        harness.World.FindCombat(West)?.GetTopHater(MobId(mob));

    private static Dictionary<string, object> Npc() =>
        WorldHarness.AsPersisted(new Dictionary<string, object> { ["type"] = "npc" });

    // -----------------------------------------------------------------------
    // What it does
    // -----------------------------------------------------------------------

    [Fact]
    public void A_taunt_takes_the_mob_off_whoever_had_it()
    {
        // The scenario in one test: an Adept out-damages the Warden and pulls the mob; the Warden
        // taunts and takes it back.
        var harness = Loaded();
        var warden = Warden(harness);
        var adept = harness.AddPlayer("Ilse", West, path: CharacterPath.Adept, level: 10);
        var rat = harness.AddMob("rat", West, health: 5_000);

        harness.Execute(warden, "kill rat");

        var combat = harness.World.GetOrCreateCombat(West);
        combat.AddToHateList(MobId(rat), PlayerId(adept), 400);
        Assert.Equal(PlayerId(adept), TopHater(harness, rat));

        harness.Execute(warden, "taunt");
        harness.Pump(2);

        Assert.Equal(PlayerId(warden), TopHater(harness, rat));
    }

    [Fact]
    public void The_mob_swings_at_the_taunter_without_waiting_for_the_next_round()
    {
        // A taunt whose whole promise is "it hits me now" cannot afford to be a swing late, and
        // the swing it would be late by is the one landing on whoever it was meant to save.
        var harness = Loaded();
        var warden = Warden(harness);
        var rat = harness.AddMob("rat", West, health: 5_000);

        harness.Execute(warden, "kill rat");
        harness.Execute(warden, "taunt");
        harness.Pump(2);

        Assert.Equal(PlayerId(warden), rat.CurrentTarget);
    }

    /// <summary>The threat one taunt buys against a mob of this size, from a standing start.</summary>
    private static int LeadAgainst(int health)
    {
        // A fresh fight each time: taunt has a 32-pulse cooldown, so measuring both in one room
        // would silently measure one taunt and one refusal.
        var harness = Loaded();
        var warden = Warden(harness);
        var mob = harness.AddMob("target", West, health: health, name: "target");

        harness.Execute(warden, "taunt target");
        harness.Pump(2);

        return HateFor(harness, mob, warden);
    }

    [Fact]
    public void The_lead_scales_with_the_target_rather_than_being_a_flat_number()
    {
        // Threat is cumulative damage and grows without bound over a fight, so a flat lead would
        // be decisive in the first ten seconds and beneath notice five minutes in. A fraction of
        // the health bar is the same promise against a rat and against a dragon: to take this mob
        // back off you, someone has to deal this much of its health in damage more than you do.
        Assert.True(LeadAgainst(10_000) > LeadAgainst(100) * 10);
    }

    [Fact]
    public void Even_a_trivial_target_yields_a_lead_worth_having()
    {
        // Rounded up, so a mob small enough that a fraction of its health is under one still
        // hands over a real position rather than a no-op.
        Assert.True(LeadAgainst(1) > 0);
    }

    [Fact]
    public void A_taunt_is_a_lead_and_not_a_lock()
    {
        // The list stays a damage meter afterwards. Holding a mob has to be something a Warden
        // keeps doing, or the ability deletes the decision it exists to create.
        var harness = Loaded();
        var warden = Warden(harness);
        var adept = harness.AddPlayer("Ilse", West, path: CharacterPath.Adept, level: 10);
        var rat = harness.AddMob("rat", West, health: 5_000);

        harness.Execute(warden, "kill rat");
        harness.Execute(warden, "taunt");
        harness.Pump(2);
        Assert.Equal(PlayerId(warden), TopHater(harness, rat));

        // The Adept keeps going and eventually takes it back.
        harness.World.FindCombat(West)!.AddToHateList(MobId(rat), PlayerId(adept), 100_000);

        Assert.Equal(PlayerId(adept), TopHater(harness, rat));
    }

    [Fact]
    public void Taunting_twice_never_costs_the_lead()
    {
        // ForceTopHater never lowers anyone, so a second taunt while already miles ahead keeps
        // what it had rather than dropping to a freshly computed number.
        var harness = Loaded();
        var warden = Warden(harness);
        var rat = harness.AddMob("rat", West, health: 500);

        harness.Execute(warden, "kill rat");
        harness.World.FindCombat(West)!.AddToHateList(MobId(rat), PlayerId(warden), 50_000);
        var before = HateFor(harness, rat, warden);

        harness.Execute(warden, "taunt");
        harness.Pump(2);

        Assert.True(HateFor(harness, rat, warden) >= before);
    }

    [Fact]
    public void A_taunt_pulls_in_a_mob_that_was_not_fighting_anyone()
    {
        // Requested explicitly: it should become aggressive to the taunter as though it had been
        // attacked, and be in combat afterwards.
        var harness = Loaded();
        var warden = Warden(harness);
        var rat = harness.AddMob("rat", West, health: 500);

        harness.Execute(warden, "taunt rat");
        harness.Pump(2);

        Assert.NotNull(harness.World.FindCombat(West));
        Assert.Equal(CombatState.Fighting, rat.CombatState);
        Assert.Equal(PlayerId(warden), TopHater(harness, rat));
    }

    [Fact]
    public void A_bare_taunt_shouts_at_what_you_are_already_fighting()
    {
        var harness = Loaded();
        var warden = Warden(harness);
        var rat = harness.AddMob("rat", West, health: 500);
        harness.AddMob("wolf", West, health: 500, name: "wolf");

        harness.Execute(warden, "kill rat");
        harness.Execute(warden, "taunt");
        harness.Pump(2);

        Assert.Equal(PlayerId(warden), rat.CurrentTarget);
    }

    // -----------------------------------------------------------------------
    // The §4.11 gate — a taunt refuses exactly where kill refuses
    // -----------------------------------------------------------------------

    [Fact]
    public void A_taunt_cannot_make_a_shopkeeper_hostile()
    {
        // The reason this gate matters more for taunt than for anything else: it is a way to
        // start a fight with something you never attacked, so without the non-combatant refusal
        // it becomes *the* way to make a quest giver killable.
        var harness = Loaded();
        var warden = Warden(harness);
        var keeper = harness.AddMob("keeper", West, health: 500, name: "keeper", behavior: Npc());

        harness.Execute(warden, "taunt keeper");
        harness.Pump(2);

        Assert.Equal(CombatState.Idle, keeper.CombatState);
        Assert.Null(harness.World.FindCombat(West)?.GetTopHater(MobId(keeper)));
    }

    [Fact]
    public void A_taunt_is_refused_in_a_peaceful_room()
    {
        var harness = Loaded();
        harness.Mutate(new SetRoomFlag(West, RoomFlags.Peaceful.Key, true));

        var warden = Warden(harness);
        var rat = harness.AddMob("rat", West, health: 500);

        harness.Execute(warden, "taunt rat");
        harness.Pump(2);

        Assert.Equal(CombatState.Idle, rat.CombatState);
    }

    [Fact]
    public void A_refused_taunt_costs_nothing()
    {
        // Refused in the command, before the cast is queued, so neither the stamina nor the
        // cooldown is spent on a shout that never happened.
        var harness = Loaded();
        var warden = Warden(harness);
        harness.AddMob("keeper", West, health: 500, name: "keeper", behavior: Npc());

        var before = warden.Character.Vitals.Stamina;
        harness.Execute(warden, "taunt keeper");
        harness.Pump(2);

        Assert.Equal(before, warden.Character.Vitals.Stamina);
        Assert.Null(harness.World.GetAbilityCooldown(warden.CharacterId, "warden.taunt"));
    }

    // -----------------------------------------------------------------------
    // The same gate, now shared with every other hostile action
    // -----------------------------------------------------------------------

    [Fact]
    public void A_damaging_cast_cannot_kill_a_shopkeeper_either()
    {
        // `kill` has always refused a non-combatant; `cast` refused nothing, so an Adept could
        // Bolt down the mob handing out the zone's quests. One gate now, so the two agree.
        var harness = Loaded();
        var adept = harness.AddPlayer("Ilse", West, path: CharacterPath.Adept, level: 10);
        adept.Character.Vitals.FocusMax = 500;
        adept.Character.Vitals.Focus = 500;
        harness.DefineAbility("adept.bolt");

        var keeper = harness.AddMob("keeper", West, health: 500, name: "keeper", behavior: Npc());

        harness.Execute(adept, "cast bolt keeper");
        harness.Pump(20);

        Assert.Equal(500, keeper.Vitals.Health);
    }

    [Fact]
    public void A_damaging_cast_is_refused_in_a_peaceful_room()
    {
        var harness = Loaded();
        harness.Mutate(new SetRoomFlag(West, RoomFlags.Peaceful.Key, true));

        var adept = harness.AddPlayer("Ilse", West, path: CharacterPath.Adept, level: 10);
        adept.Character.Vitals.FocusMax = 500;
        adept.Character.Vitals.Focus = 500;
        harness.DefineAbility("adept.bolt");

        var rat = harness.AddMob("rat", West, health: 500);

        harness.Execute(adept, "cast bolt rat");
        harness.Pump(20);

        Assert.Equal(500, rat.Vitals.Health);
    }

    [Fact]
    public void A_heal_still_works_in_a_peaceful_room()
    {
        // The gate is for hostile actions only. Peaceful forbids combat, not medicine - gating
        // both on the one flag would make a safe room the one place a support Path cannot work.
        var harness = Loaded();
        harness.Mutate(new SetRoomFlag(West, RoomFlags.Peaceful.Key, true));

        var hallow = harness.AddPlayer("Bram", West, path: CharacterPath.Hallow, level: 10);
        hallow.Character.Vitals.FocusMax = 500;
        hallow.Character.Vitals.Focus = 500;
        hallow.Character.Vitals.Health = 5;
        harness.DefineAbility("hallow.mend");

        harness.Execute(hallow, "cast mend Bram");
        harness.Pump(8);

        Assert.True(hallow.Character.Vitals.Health > 5);
    }
}
