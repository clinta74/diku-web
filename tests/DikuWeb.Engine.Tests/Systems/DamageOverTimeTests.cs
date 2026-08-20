using DikuWeb.Domain.Abilities.Effects;
using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Entities;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Tests.Infrastructure;

namespace DikuWeb.Engine.Tests.Systems;

/// <summary>
/// Bleeds, burns, and withers — the fifth effect, and the first that puts damage on a clock of
/// its own rather than scaling a number at the moment of a swing.
/// </summary>
/// <remarks>
/// The tick lives inside the combat loop, where the death, XP, and loot paths already are. The
/// consequence worth pinning is that wounds only work during a fight: a bleed cannot follow
/// someone out of the room, which falls out of §4.11's rule that leaving ends the fight.
/// </remarks>
public sealed class DamageOverTimeTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");

    /// <summary>A Temper mid-fight with a rat, ready to open a wound.</summary>
    private static (WorldHarness Harness, Engine.World.PlayerActor Player, Domain.Inhabitants.Mob Rat) Fight(
        int ratHealth = 1000)
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.DefineAbility("temper.body-blow");

        var player = harness.AddPlayer("Vex", West, path: CharacterPath.Temper, level: 10);
        player.Character.Vitals.Stamina = 500;

        // At the player's level, so any test in here that checks a reward is measuring the wound
        // rather than the relevance window (§4.7). The effective level is snapshotted when the mob
        // is created, so setting Level afterwards would not reach it.
        var rat = harness.AddMob("rat", West, health: ratHealth, level: 10);

        harness.Execute(player, "kill rat");
        harness.Drain(player);

        return (harness, player, rat);
    }

    /// <summary>Puts a wound straight onto the mob, bypassing the cast so timing is exact.</summary>
    private static void Wound(
        WorldHarness harness,
        Domain.Inhabitants.Mob mob,
        int tickDamage,
        long interval,
        long duration,
        int stacks = 1)
    {
        harness.World.ApplyEffect(mob.Id, new ActiveEffect
        {
            EffectKey = "damage.overtime",
            Name = "bleeding",
            SourceEntityId = "c_test",
            TickDamage = tickDamage,
            TickIntervalPulses = interval,
            NextTickPulse = harness.Clock.CurrentPulse + interval,
            ExpiresAtPulse = harness.Clock.CurrentPulse + duration,
            Stacks = stacks,
            MaxStacks = 3,
        });
    }

    [Fact]
    public void A_wound_deals_its_damage_on_the_interval()
    {
        var (harness, _, rat) = Fight();
        Wound(harness, rat, tickDamage: 10, interval: 4, duration: 100);
        var before = rat.Vitals.Health;

        // Pump(n) ticks pulses 0..n-1, so reaching the pulse-4 tick takes five.
        harness.Pump(5);

        Assert.True(before - rat.Vitals.Health >= 10);
    }

    [Fact]
    public void A_wound_does_not_land_on_the_pulse_it_is_applied()
    {
        // Otherwise a bleed deals its opening damage on the same pulse as the blow that caused
        // it, and reads to a player as the strike hitting twice.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var player = harness.AddPlayer("Vex", West, path: CharacterPath.Temper, level: 10);
        var rat = harness.AddMob("rat", West, health: 1000);
        harness.Execute(player, "kill rat");

        Wound(harness, rat, tickDamage: 10, interval: 8, duration: 100);
        var before = rat.Vitals.Health;

        harness.Pump(1);

        Assert.Equal(before, rat.Vitals.Health);
    }

    [Fact]
    public void A_wound_ticks_repeatedly_until_it_expires()
    {
        var (harness, _, rat) = Fight();
        Wound(harness, rat, tickDamage: 10, interval: 4, duration: 12);
        var before = rat.Vitals.Health;

        harness.Pump(40);

        // Ticks at 4 and 8. The one due at 12 is skipped, because the effect expires *at* 12 and
        // a tick landing on the expiry pulse would be a free extra one. The rat is still being
        // hit by the player, so this is a floor rather than an equality.
        Assert.True(before - rat.Vitals.Health >= 20);
    }

    [Fact]
    public void A_wound_stops_when_it_expires()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var player = harness.AddPlayer("Vex", West, path: CharacterPath.Temper, level: 10);
        var rat = harness.AddMob("rat", West, health: 1000);
        harness.Execute(player, "kill rat");

        // Ticks at 4 and 8, then expires. Nothing removes it in the harness - the expiry sweep
        // runs on the 60s tick - so this pins that the *ticker* respects the expiry rather than
        // relying on the sweep to have got there first.
        // One tick at 4 for a thousand; the tick due at 8 is the expiry pulse and is skipped.
        // If the ticker ignored expiry it would fire ~50 times over the pump below and the rat
        // would be long dead, so survival is the signal.
        var rat5k = rat;
        rat5k.Vitals.HealthMax = 5000;
        rat5k.Vitals.Health = 5000;
        Wound(harness, rat, tickDamage: 1000, interval: 4, duration: 8);

        harness.Pump(200);

        Assert.True(
            rat.Vitals.Health > 0,
            $"The wound kept ticking past its expiry; the rat is on {rat.Vitals.Health}.");
    }

    [Fact]
    public void Stacks_multiply_the_wound_rather_than_running_their_own_clocks()
    {
        var (harness, _, rat) = Fight();
        Wound(harness, rat, tickDamage: 10, interval: 4, duration: 100, stacks: 3);
        var before = rat.Vitals.Health;

        harness.Pump(5);

        Assert.True(before - rat.Vitals.Health >= 30, "Three stacks should tick for thirty.");
    }

    [Fact]
    public void A_wound_can_land_the_killing_blow()
    {
        // The reason the ticker lives in the combat loop. A bleed that could not finish something
        // would be a strange kind of wound, and the XP and loot have to come with it.
        var (harness, player, rat) = Fight(ratHealth: 12);
        var xpBefore = player.Character.Xp;
        rat.ResolvedXp = 50;

        Wound(harness, rat, tickDamage: 20, interval: 4, duration: 100);
        harness.Pump(8);

        Assert.True(rat.Vitals.Health <= 0);
        Assert.True(player.Character.Xp > xpBefore, "A kill by bleed should still award XP.");
    }

    [Fact]
    public void A_wound_is_narrated_to_the_room()
    {
        var (harness, player, rat) = Fight();
        harness.Drain(player);
        Wound(harness, rat, tickDamage: 10, interval: 4, duration: 100);

        harness.Pump(5);

        Assert.Contains("bleeding", harness.DrainText(player), StringComparison.Ordinal);
    }

    [Fact]
    public void A_wound_on_a_player_is_narrated_in_the_second_person()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var player = harness.AddPlayer("Vex", West, path: CharacterPath.Temper, level: 10);
        var rat = harness.AddMob("rat", West, health: 1000);
        harness.Execute(player, "kill rat");
        harness.Drain(player);

        harness.World.ApplyEffect(player.CharacterId, new ActiveEffect
        {
            EffectKey = "damage.overtime",
            Name = "bleeding",
            SourceEntityId = EntityId.ForMob(rat.Id),
            TickDamage = 3,
            TickIntervalPulses = 4,
            NextTickPulse = harness.Clock.CurrentPulse + 4,
            ExpiresAtPulse = harness.Clock.CurrentPulse + 100,
        });

        harness.Pump(5);

        Assert.Contains("Your bleeding costs you", harness.DrainText(player), StringComparison.Ordinal);
    }

    [Fact]
    public void Casting_the_ability_applies_the_wound()
    {
        // End to end through the command, so the catalogue's parameter names are exercised
        // rather than the hand-built effect the other tests use.
        var (harness, player, rat) = Fight();

        harness.Execute(player, "body blow rat");
        harness.Pump(2);

        var effects = harness.World.GetActiveEffects(rat.Id);
        Assert.Contains(effects, e => e.EffectKey == "damage.overtime" && e.Ticks);
    }
}
