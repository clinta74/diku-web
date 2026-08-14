using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Entities;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Tests.Infrastructure;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Tests.Systems;

/// <summary>
/// An ability is a way to open a fight, and a way to finish one (PLAN.md §4.2, §4.7).
/// </summary>
/// <remarks>
/// Abilities resolve in their own system, earlier in the pulse than combat, and they used to write
/// to <c>Vitals.Health</c> and stop. Both ends of a fight were missing as a result:
///
/// <list type="bullet">
/// <item><description><b>The opening.</b> A kick landed, and the player stood there. The mob was
/// engaged, so it swung back, but nothing had pointed the player's own weapon at anything - so the
/// one command a level-1 Warden has to start a fight with did not start one. A pure bleed was worse
/// still: it dealt no immediate damage, so nothing engaged at all, and since wounds only tick
/// inside a combat the wound never ticked.</description></item>
/// <item><description><b>The ending.</b> A mob killed by an ability sat in the fight at zero
/// health for ever. It could not swing, so the fight never fell below two combatants and never
/// ended; no experience, no loot, no corpse, and a player stuck <c>Fighting</c> who could not even
/// <c>kill</c> again.</description></item>
/// </list>
/// </remarks>
public sealed class AbilityCombatTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");

    private static WorldHarness Loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return harness;
    }

    private static PlayerActor Fighter(
        WorldHarness harness,
        string abilityKey,
        string name = "Theron",
        CharacterPath path = CharacterPath.Warden,
        int level = 12)
    {
        var actor = harness.AddPlayer(name, West, path: path, level: level);
        actor.Character.Vitals.StaminaMax = 500;
        actor.Character.Vitals.Stamina = 500;
        actor.Character.Vitals.FocusMax = 500;
        actor.Character.Vitals.Focus = 500;
        harness.DefineAbility(abilityKey);
        return actor;
    }

    // -----------------------------------------------------------------------
    // Opening a fight
    // -----------------------------------------------------------------------

    [Fact]
    public void An_ability_used_out_of_combat_starts_the_fight()
    {
        var harness = Loaded();
        var warden = Fighter(harness, "warden.kick");
        var rat = harness.AddMob("rat", West, health: 500);

        harness.Execute(warden, "kick rat");
        harness.Pump(4);

        Assert.Equal(CombatState.Fighting, warden.Character.CombatState);
        Assert.Equal(CombatState.Fighting, rat.CombatState);
        Assert.Equal(EntityId.ForMob(rat.Id), warden.Character.CurrentTarget);
    }

    [Fact]
    public void And_the_player_keeps_swinging_afterwards()
    {
        // The assertion the state check alone cannot make: an ability that set CurrentTarget but
        // left PlayerTargets empty would pass everything above and still leave the player standing
        // there, because the combat loop reads the fight's copy rather than the character's.
        var harness = Loaded();
        var warden = Fighter(harness, "warden.kick");
        var rat = harness.AddMob("rat", West, health: 500);

        harness.Execute(warden, "kick rat");
        harness.Pump(4);

        var afterTheKick = rat.Vitals.Health;
        harness.Pump(40);

        Assert.True(rat.Vitals.Health < afterTheKick);
    }

    [Fact]
    public void A_wound_opened_out_of_combat_still_ticks()
    {
        // Ambush deals nothing on application - the whole ability is the bleed. Engagement used to
        // be keyed on damage dealt, so the opener of the Path built around wounds engaged nothing,
        // and a wound outside a fight is a wound that never ticks.
        var harness = Loaded();
        var shade = Fighter(harness, "shade.ambush", name: "Vess", path: CharacterPath.Shade);
        harness.AddMob("rat", West, health: 500);

        harness.Execute(shade, "ambush rat");
        harness.Pump(60);

        Assert.Contains("bleeding", harness.DrainText(shade), StringComparison.Ordinal);
    }

    [Fact]
    public void An_ability_that_moves_no_health_still_starts_the_fight()
    {
        // A stun is hostile whatever it did to the health bar, and something you have just stunned
        // is something you are fighting.
        var harness = Loaded();
        var warden = Fighter(harness, "warden.shield-bash");
        var rat = harness.AddMob("rat", West, health: 500);

        harness.Execute(warden, "shield bash rat");
        harness.Pump(4);

        Assert.NotNull(harness.World.FindCombat(West));
        Assert.Equal(CombatState.Fighting, rat.CombatState);
    }

    [Fact]
    public void An_ability_does_not_switch_a_target_the_player_already_chose()
    {
        // Throwing something at the second mob in the room is not a request to turn your back on
        // the first one. A silent target switch mid-fight is a good way to die.
        var harness = Loaded();
        var warden = Fighter(harness, "warden.kick");
        var rat = harness.AddMob("rat", West, health: 500);
        harness.AddMob("wolf", West, health: 500, name: "wolf");

        harness.Execute(warden, "kill rat");
        harness.Execute(warden, "kick wolf");
        harness.Pump(4);

        Assert.Equal(EntityId.ForMob(rat.Id), warden.Character.CurrentTarget);
    }

    [Fact]
    public void A_heal_starts_nothing()
    {
        var harness = Loaded();
        var hallow = Fighter(harness, "hallow.mend", name: "Bram", path: CharacterPath.Hallow);
        harness.AddMob("rat", West, health: 500);

        hallow.Character.Vitals.Health = 5;
        harness.Execute(hallow, "cast mend");
        harness.Pump(8);

        Assert.Null(harness.World.FindCombat(West));
        Assert.Equal(CombatState.Idle, hallow.Character.CombatState);
    }

    // -----------------------------------------------------------------------
    // Finishing one
    // -----------------------------------------------------------------------

    [Fact]
    public void A_mob_killed_by_an_ability_leaves_the_world()
    {
        var harness = Loaded();
        var warden = Fighter(harness, "warden.kick");
        var rat = harness.AddMob("rat", West, health: 1);

        harness.Execute(warden, "kick rat");
        harness.Pump(4);

        Assert.Null(harness.World.GetMob(rat.Id));
        Assert.Contains("falls", harness.DrainText(warden), StringComparison.Ordinal);
    }

    [Fact]
    public void And_the_fight_it_was_in_ends()
    {
        // The reported bug: the combat loop never came back round, because a fight only ends when
        // it drops below two combatants and a corpse at zero health was still one of them.
        var harness = Loaded();
        var warden = Fighter(harness, "warden.kick");
        harness.AddMob("rat", West, health: 1);

        harness.Execute(warden, "kick rat");
        harness.Pump(4);

        Assert.Null(harness.World.FindCombat(West));
        Assert.Equal(CombatState.Idle, warden.Character.CombatState);
        Assert.Null(warden.Character.CurrentTarget);
    }

    [Fact]
    public void And_the_player_can_start_another_one()
    {
        // What a player actually notices about a fight that never ended: every later `kill` is
        // refused with "You're already in combat!" until they log out.
        var harness = Loaded();
        var warden = Fighter(harness, "warden.kick");
        harness.AddMob("rat", West, health: 1);
        harness.AddMob("wolf", West, health: 500, name: "wolf");

        harness.Execute(warden, "kick rat");
        harness.Pump(4);
        harness.Drain(warden);

        harness.Execute(warden, "kill wolf");

        Assert.DoesNotContain(
            "already in combat", harness.DrainText(warden), StringComparison.Ordinal);
    }

    [Fact]
    public void A_kill_landed_by_an_ability_pays_the_same_as_one_landed_by_a_swing()
    {
        // Experience and gold hang off HandleDeath, so a death the combat loop never resolved was
        // also a kill that never paid.
        var harness = Loaded();
        var warden = Fighter(harness, "warden.kick");
        // Level 12 to match the fighter: this test is about a kill paying at all, and a level 1
        // rat would pay nothing for a reason that has nothing to do with abilities (§5.3).
        var rat = harness.AddMob("rat", West, health: 1, level: 12);
        rat.ResolvedXp = 40;
        rat.ResolvedGold = 7;

        var xpBefore = warden.Character.Xp;
        var goldBefore = warden.Character.Gold;

        harness.Execute(warden, "kick rat");
        harness.Pump(4);

        Assert.Equal(xpBefore + 40, warden.Character.Xp);
        Assert.Equal(goldBefore + 7, warden.Character.Gold);
    }

    [Fact]
    public void A_wound_that_lands_the_killing_blow_ends_the_fight_too()
    {
        // The other half of the note: whatever finishes something, it should finish the same way.
        var harness = Loaded();
        var shade = Fighter(harness, "shade.ambush", name: "Vess", path: CharacterPath.Shade);
        var rat = harness.AddMob("rat", West, health: 1);
        rat.ResolvedXp = 40;

        harness.Execute(shade, "ambush rat");
        harness.Pump(60);

        Assert.Null(harness.World.GetMob(rat.Id));
        Assert.Null(harness.World.FindCombat(West));
        Assert.Equal(CombatState.Idle, shade.Character.CombatState);
    }
}
