using Muwbta.Domain.Abilities.Effects;
using Muwbta.Domain.Characters;
using Muwbta.Domain.Combat;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Tests.Infrastructure;

namespace Muwbta.Engine.Tests.Systems;

/// <summary>
/// Snare — the seventh effect, and the counterpart to the stun: it leaves the turn and closes
/// the exit.
/// </summary>
/// <remarks>
/// What it actually denies is <c>flee</c>. Ordinary movement is already refused while fighting,
/// so a root that only blocked walking would do nothing at all in the one situation it is ever
/// cast in — which is the trap these tests exist to catch.
/// </remarks>
public sealed class RootTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");

    private static void Root(WorldHarness harness, Guid entityId, long duration = 32)
    {
        harness.World.ApplyEffect(entityId, new ActiveEffect
        {
            EffectKey = "control.root",
            // What Low Kick applies. The mob attack in MobAttackEffectTests still hamstrings,
            // and should: a mob with a blade can, and this Path no longer carries one.
            Name = "hobbled",
            SourceEntityId = "c_test",
            PreventsEscape = true,
            ExpiresAtPulse = harness.Clock.CurrentPulse + duration,
        });
    }

    [Fact]
    public void A_snared_character_cannot_flee()
    {
        // The whole point of the effect.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var player = harness.AddPlayer("Theron", West);
        harness.AddMob("rat", West, health: 10_000);
        harness.Execute(player, "kill rat");

        Root(harness, player.CharacterId);
        harness.Drain(player);

        harness.Execute(player, "flee");

        Assert.Equal(CombatState.Fighting, player.Character.CombatState);
        Assert.Contains("cannot break away", harness.DrainText(player), StringComparison.Ordinal);
    }

    [Fact]
    public void An_unsnared_character_can_still_flee()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var player = harness.AddPlayer("Theron", West);
        harness.AddMob("rat", West, health: 10_000);
        harness.Execute(player, "kill rat");
        harness.Drain(player);

        harness.Execute(player, "flee");

        Assert.Equal(CombatState.Idle, player.Character.CombatState);
    }

    [Fact]
    public void The_snare_lets_go_when_it_expires()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var player = harness.AddPlayer("Theron", West);
        harness.AddMob("rat", West, health: 10_000);
        harness.Execute(player, "kill rat");

        Root(harness, player.CharacterId, duration: 4);
        harness.Pump(8);
        harness.Drain(player);

        harness.Execute(player, "flee");

        Assert.Equal(CombatState.Idle, player.Character.CombatState);
    }

    [Fact]
    public void A_snared_character_cannot_walk_away_either()
    {
        // Out of combat, where the movement gate is the only one that applies.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var player = harness.AddPlayer("Theron", West);
        Root(harness, player.CharacterId);
        harness.Drain(player);

        harness.Execute(player, "east");

        Assert.Equal(West, player.RoomKey);
        Assert.Contains("cannot go anywhere", harness.DrainText(player), StringComparison.Ordinal);
    }

    [Fact]
    public void An_unsnared_character_walks_normally()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var player = harness.AddPlayer("Theron", West);

        harness.Execute(player, "east");

        Assert.NotEqual(West, player.RoomKey);
    }

    [Fact]
    public void A_snare_does_not_stop_the_target_fighting_back()
    {
        // The line between a snare and a stun. Rooting something that then stood there doing
        // nothing would make the two effects the same, and there would be no reason for both.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var player = harness.AddPlayer("Theron", West, level: 1);
        var rat = harness.AddMob(
            "rat", West, attacks: [new Domain.Inhabitants.MobAttack { Verb = "bite", DelayPulses = 4 }],
            health: 100_000, damageMin: 5, damageMax: 5);

        harness.Execute(player, "kill rat");
        harness.Drain(player);

        Root(harness, rat.Id, duration: 400);
        var before = player.Character.Vitals.Health;

        harness.Pump(30);

        Assert.True(player.Character.Vitals.Health < before, "A snared rat should still bite.");
    }

    [Fact]
    public void A_snared_character_can_still_cast()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.DefineAbility("adept.bolt");

        var caster = harness.AddPlayer("Mira", West, path: CharacterPath.Adept, level: 5);
        caster.Character.Vitals.Focus = 100;
        var rat = harness.AddMob("rat", West, health: 500);
        var before = rat.Vitals.Health;

        Root(harness, caster.CharacterId, duration: 400);

        harness.Execute(caster, "cast bolt rat");
        harness.Pump(12);

        Assert.True(rat.Vitals.Health < before, "A snare takes the exit, not the turn.");
    }

    [Fact]
    public void An_over_long_snare_is_clamped()
    {
        var effect = new RootEffect();
        var caster = WorldHarness.NewCharacter("Theron", West);

        var active = effect.CreateActiveEffect(
            caster,
            caster,
            new Dictionary<string, string> { ["durationPulses"] = "99999" },
            currentPulse: 0);

        Assert.Equal(RootEffect.MaxDurationPulses, active.ExpiresAtPulse);
    }

    [Fact]
    public void Casting_low_kick_snares_the_target()
    {
        // End to end, so the shipped ability's parameter names are exercised rather than the
        // hand-built effect the other tests use.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.DefineAbility("temper.low-kick");

        var temper = harness.AddPlayer("Vex", West, path: CharacterPath.Temper, level: 7);
        temper.Character.Vitals.Stamina = 200;
        var rat = harness.AddMob("rat", West, health: 500);

        harness.Execute(temper, "low kick rat");
        harness.Pump(2);

        Assert.True(harness.World.IsRooted(rat.Id, harness.Clock.CurrentPulse));
    }
}
