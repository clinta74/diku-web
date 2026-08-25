using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Tests.Infrastructure;

namespace DikuWeb.Engine.Tests.Systems;

/// <summary>
/// Parry in an actual fight: a blow that would have landed is turned aside instead.
/// </summary>
/// <remarks>
/// The roll is pinned by the harness's random source rather than inferred from a seed, so each
/// test states plainly whether the parry fires. Without that, "the Warden took less damage" could
/// equally be a damage roll going the other way.
/// </remarks>
public sealed class ParryCombatTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");

    /// <summary>
    /// A mob that swings every four pulses for a fixed 5, against a player of the given Path and
    /// level. The player never swings back, so the only narration is the mob's.
    /// </summary>
    private static (WorldHarness Harness, Engine.World.PlayerActor Player, Mob Mob) Fight(
        CharacterPath path,
        int level,
        ScriptedChanceSource random)
    {
        var harness = new WorldHarness(random);
        harness.LoadTestWorld();

        var player = harness.AddPlayer("Theron", West, path: path, level: level);
        var mob = harness.AddMob(
            "rat",
            West,
            attacks: [new MobAttack { Verb = "bite", DelayPulses = 4 }],
            health: 100_000,
            damageMin: 5,
            damageMax: 5);

        harness.Execute(player, "kill rat");
        harness.Drain(player);

        return (harness, player, mob);
    }

    private static string PumpAndRead(WorldHarness harness, Engine.World.PlayerActor player, int pulses)
    {
        var log = new System.Text.StringBuilder();

        for (var i = 0; i < pulses; i++)
        {
            harness.Pump();
            log.Append(harness.DrainText(player));
        }

        return log.ToString();
    }

    [Fact]
    public void A_warden_who_has_learned_to_parry_turns_a_blow_aside()
    {
        var (harness, player, _) = Fight(CharacterPath.Warden, 4, ScriptedChanceSource.Always);

        var log = PumpAndRead(harness, player, 12);

        Assert.Contains("parries", log, StringComparison.Ordinal);
    }

    [Fact]
    public void A_parried_blow_deals_no_damage()
    {
        // The point of the whole thing. A parry that narrated but still hurt would be worse than
        // no parry, because the player would believe they were defended.
        var (harness, player, _) = Fight(CharacterPath.Warden, 4, ScriptedChanceSource.Always);
        var before = player.Character.Vitals.Health;

        PumpAndRead(harness, player, 12);

        Assert.Equal(before, player.Character.Vitals.Health);
    }

    [Fact]
    public void An_unparried_blow_still_lands()
    {
        var (harness, player, _) = Fight(CharacterPath.Warden, 4, ScriptedChanceSource.LandsUnparried);
        var before = player.Character.Vitals.Health;

        var log = PumpAndRead(harness, player, 12);

        Assert.DoesNotContain("parries", log, StringComparison.Ordinal);
        Assert.True(player.Character.Vitals.Health < before, "The rat should have drawn blood.");
    }

    [Fact]
    public void A_warden_below_the_unlock_level_does_not_parry()
    {
        // Even with the roll forced to succeed: the chance is zero, so it is never rolled against.
        var (harness, player, _) = Fight(CharacterPath.Warden, 3, ScriptedChanceSource.Always);
        var before = player.Character.Vitals.Health;

        var log = PumpAndRead(harness, player, 12);

        Assert.DoesNotContain("parries", log, StringComparison.Ordinal);
        Assert.True(player.Character.Vitals.Health < before);
    }

    [Fact]
    public void A_blade_parries_once_it_has_learned_how()
    {
        var (harness, player, _) = Fight(CharacterPath.Temper, 8, ScriptedChanceSource.Always);

        Assert.Contains("parries", PumpAndRead(harness, player, 12), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CharacterPath.Adept)]
    [InlineData(CharacterPath.Hallow)]
    public void A_casting_path_never_parries(CharacterPath path)
    {
        var (harness, player, _) = Fight(path, 20, ScriptedChanceSource.Always);
        var before = player.Character.Vitals.Health;

        var log = PumpAndRead(harness, player, 12);

        Assert.DoesNotContain("parries", log, StringComparison.Ordinal);
        Assert.True(player.Character.Vitals.Health < before);
    }

    [Fact]
    public void A_parry_does_not_end_the_fight()
    {
        // The swing is spent and the exchange continues; a parry must not read as a disengage.
        var (harness, player, mob) = Fight(CharacterPath.Warden, 4, ScriptedChanceSource.Always);

        PumpAndRead(harness, player, 12);

        Assert.Equal(Domain.Combat.CombatState.Fighting, player.Character.CombatState);
        Assert.Equal(Domain.Combat.CombatState.Fighting, mob.CombatState);
    }

    [Fact]
    public void A_mob_never_parries_the_players_blow()
    {
        // Parry comes from Path and level, and a mob has neither. Giving it to mobs would make
        // every fight longer without making any of them more interesting.
        var harness = new WorldHarness(ScriptedChanceSource.Always);
        harness.LoadTestWorld();

        var player = harness.AddPlayer("Theron", West, path: CharacterPath.Warden, level: 4);
        var sword = harness.DefineWeapon("sword", "a sword", Domain.Items.ItemSlot.MainHand, 4, "slash");
        harness.Equip(player, sword, Domain.Items.ItemSlot.MainHand);
        harness.AddMob("rat", West, health: 100_000);

        harness.Execute(player, "kill rat");
        harness.Drain(player);

        var log = PumpAndRead(harness, player, 12);

        Assert.DoesNotContain("rat parries", log, StringComparison.Ordinal);
    }
}
