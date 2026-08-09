using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Tests.Infrastructure;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Tests.Systems;

/// <summary>
/// A mob attack that does something other than damage (PLAN.md §12).
/// </summary>
/// <remarks>
/// Every one of the seven executors was a player-only tool, and the asymmetry was live rather
/// than theoretical: a Warden's Shield Bash takes a boss off its feet for three seconds and the
/// boss had no answer of any kind.
///
/// What closed it is deliberately <em>not</em> a spellbook. A mob has no cast bar, no focus pool,
/// and no ability list to work through — it has attacks, and an attack can carry an effect. That
/// is most of why this was cheap: the swing already has its own timer, already rolls to hit, and
/// already resolves damage, so the effect inherits the miss chance, the parry, and the death
/// check for free. The receiving side had been finished for players all along.
/// </remarks>
public sealed class MobAttackEffectTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");

    private static WorldHarness Loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return harness;
    }

    private static MobAttack Attack(
        string verb,
        string? effectKey = null,
        Dictionary<string, string>? parameters = null,
        int delay = 4) => new()
        {
            Verb = verb,
            DelayPulses = delay,
            EffectKey = effectKey,
            EffectParams = parameters,
        };

    /// <summary>A mob that always connects, so an assertion is about the effect and not the dice.</summary>
    private static Mob Attacker(WorldHarness harness, MobAttack attack, int damage = 1) =>
        harness.AddMob(
            "ogre", West, attacks: [attack], health: 100_000,
            name: "ogre", damageMin: damage, damageMax: damage);

    // -----------------------------------------------------------------------
    // It lands
    // -----------------------------------------------------------------------

    [Fact]
    public void A_mob_attack_can_stun_a_player()
    {
        var harness = Loaded();
        var player = harness.AddPlayer("Theron", West, level: 20);
        player.Character.Vitals.HealthMax = 10_000;
        player.Character.Vitals.Health = 10_000;

        Attacker(harness, Attack(
            "slam", "control.stun",
            new Dictionary<string, string> { ["durationPulses"] = "12", ["name"] = "reeling" }));

        harness.Execute(player, "kill ogre");
        harness.Pump(10);

        Assert.True(harness.World.IsStunned(player.CharacterId, harness.Clock.CurrentPulse));
    }

    [Fact]
    public void A_stunned_player_cannot_cast()
    {
        // The receiving side that already existed. This is the whole point of the feature: the
        // gate `cast` checks does not care where the stun came from.
        var harness = Loaded();
        var player = harness.AddPlayer("Ilse", West, path: CharacterPath.Adept, level: 20);
        player.Character.Vitals.HealthMax = 10_000;
        player.Character.Vitals.Health = 10_000;
        player.Character.Vitals.FocusMax = 500;
        player.Character.Vitals.Focus = 500;
        harness.DefineAbility("adept.bolt");

        Attacker(harness, Attack(
            "slam", "control.stun",
            new Dictionary<string, string> { ["durationPulses"] = "24", ["name"] = "reeling" }));

        harness.Execute(player, "kill ogre");
        harness.Pump(10);
        harness.DrainText(player);

        harness.Execute(player, "cast bolt");

        Assert.Contains("cannot gather yourself", harness.DrainText(player), StringComparison.Ordinal);
    }

    [Fact]
    public void A_mob_attack_can_root_a_player_so_they_cannot_flee()
    {
        var harness = Loaded();
        var player = harness.AddPlayer("Theron", West, level: 20);
        player.Character.Vitals.HealthMax = 10_000;
        player.Character.Vitals.Health = 10_000;

        Attacker(harness, Attack(
            "hamstring", "control.root",
            new Dictionary<string, string> { ["durationPulses"] = "40", ["name"] = "hamstrung" }));

        harness.Execute(player, "kill ogre");
        harness.Pump(10);

        Assert.True(harness.World.IsRooted(player.CharacterId, harness.Clock.CurrentPulse));
    }

    [Fact]
    public void A_mob_attack_can_open_a_wound_that_keeps_working()
    {
        var harness = Loaded();
        var player = harness.AddPlayer("Theron", West, level: 20);
        player.Character.Vitals.HealthMax = 10_000;
        player.Character.Vitals.Health = 10_000;

        // The swing itself deals nothing, so every point lost is the wound working. A long delay
        // would not isolate it any better - the first swing comes one delay after engaging, so
        // the mob would simply never attack inside the test.
        harness.AddMob(
            "ogre", West,
            attacks: [Attack("rake", "damage.overtime", new Dictionary<string, string>
            {
                ["tickDamage"] = "7",
                ["tickIntervalPulses"] = "4",
                ["durationPulses"] = "80",
                ["name"] = "bleeding",
            })],
            health: 100_000, name: "ogre", damageMin: 0, damageMax: 0);

        harness.Execute(player, "kill ogre");
        harness.Pump(20);

        Assert.True(player.Character.Vitals.Health < 10_000);
    }

    [Fact]
    public void The_room_is_told_what_happened()
    {
        // Without narration a stun is invisible: the next command is refused with "You cannot
        // gather yourself" and nothing ever explained why.
        var harness = Loaded();
        var player = harness.AddPlayer("Theron", West, level: 20);
        player.Character.Vitals.HealthMax = 10_000;
        player.Character.Vitals.Health = 10_000;

        Attacker(harness, Attack(
            "slam", "control.stun",
            new Dictionary<string, string> { ["durationPulses"] = "12", ["name"] = "reeling" }));

        harness.Execute(player, "kill ogre");
        harness.Pump(10);

        Assert.Contains("You are reeling!", harness.DrainText(player), StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // It does not land
    // -----------------------------------------------------------------------

    [Fact]
    public void A_plain_attack_applies_nothing()
    {
        var harness = Loaded();
        var player = harness.AddPlayer("Theron", West, level: 20);
        player.Character.Vitals.HealthMax = 10_000;
        player.Character.Vitals.Health = 10_000;

        Attacker(harness, Attack("bite"));

        harness.Execute(player, "kill ogre");
        harness.Pump(10);

        Assert.Empty(harness.World.GetActiveEffects(player.CharacterId));
    }

    [Fact]
    public void An_effect_key_no_executor_answers_to_is_ignored()
    {
        // Absence is the safe value, as with room flags: a template naming an effect this build
        // does not have should swing for its damage rather than stop swinging.
        var harness = Loaded();
        var player = harness.AddPlayer("Theron", West, level: 20);
        player.Character.Vitals.HealthMax = 10_000;
        player.Character.Vitals.Health = 10_000;

        Attacker(harness, Attack("slam", "control.disintegrate"), damage: 5);

        harness.Execute(player, "kill ogre");
        harness.Pump(10);

        Assert.Empty(harness.World.GetActiveEffects(player.CharacterId));
        Assert.True(player.Character.Vitals.Health < 10_000);
    }

    [Fact]
    public void A_blank_effect_key_is_the_same_as_none()
    {
        // An editor writing "" for an untouched dropdown would otherwise have every swing look up
        // an effect named nothing.
        var resolved = MobAttackResolver.Resolve(new MobTemplate
        {
            Key = "ogre",
            Name = "ogre",
            Icon = "o",
            Attacks = [Attack("slam", "   ")],
        });

        Assert.Null(Assert.Single(resolved).EffectKey);
    }

    [Fact]
    public void The_killing_blow_applies_no_effect()
    {
        // Stunning something the same blow just killed is wasted work, and a bleed on a corpse
        // would tick against a dead thing.
        var harness = Loaded();
        var player = harness.AddPlayer("Theron", West, level: 1);
        player.Character.Vitals.HealthMax = 4;
        player.Character.Vitals.Health = 4;

        Attacker(harness, Attack(
            "slam", "control.stun",
            new Dictionary<string, string> { ["durationPulses"] = "24", ["name"] = "reeling" }),
            damage: 5_000);

        harness.Execute(player, "kill ogre");
        harness.Pump(12);

        Assert.Empty(harness.World.GetActiveEffects(player.CharacterId));
    }

    // -----------------------------------------------------------------------
    // Round-tripping
    // -----------------------------------------------------------------------

    [Fact]
    public void An_authored_effect_survives_the_shape_storage_returns()
    {
        // `attacks` is jsonb. The parameters are strings on purpose - a number would come back as
        // a JsonElement and quietly stop matching, which is the trap that has already killed
        // three features in this codebase.
        var attack = Attack(
            "slam", "control.stun",
            new Dictionary<string, string> { ["durationPulses"] = "12", ["name"] = "reeling" });

        var json = System.Text.Json.JsonSerializer.Serialize(new[] { attack });
        var restored = System.Text.Json.JsonSerializer.Deserialize<List<MobAttack>>(json)!;

        var resolved = MobAttackResolver.Resolve(new MobTemplate
        {
            Key = "ogre",
            Name = "ogre",
            Icon = "o",
            Attacks = restored,
        });

        var single = Assert.Single(resolved);
        Assert.Equal("control.stun", single.EffectKey);
        Assert.Equal("12", single.EffectParams!["durationPulses"]);
    }
}
