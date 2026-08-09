using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Tests.Infrastructure;

namespace DikuWeb.Engine.Tests.Commands;

/// <summary>
/// What <c>cast</c> aims at.
/// </summary>
/// <remarks>
/// The whole command path was untested: <c>WorldHarness</c> left <c>AbilityCache</c> null, so
/// every cast answered "not configured" and returned, and the buff tests reach past the command
/// layer to apply effects to the world directly. Nothing exercised target resolution at all.
/// </remarks>
public sealed class CastTargetingTests
{
    private static readonly RoomKey Room = RoomKey.Parse("test.zone.west");

    private static WorldHarness Loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return harness;
    }

    /// <summary>An Adept who knows Bolt, and a rat to point it at.</summary>
    private static (WorldHarness Harness, Engine.World.PlayerActor Caster, Domain.Inhabitants.Mob Rat) Ready()
    {
        var harness = Loaded();
        harness.DefineAbility("adept.bolt");

        var caster = harness.AddPlayer("Mira", Room, path: CharacterPath.Adept, level: 5);
        caster.Character.Vitals.Focus = 100;

        var rat = harness.AddMob("rat", Room, name: "rat", health: 200);

        return (harness, caster, rat);
    }

    [Fact]
    public void An_offensive_ability_names_a_mob_as_its_target()
    {
        var (harness, caster, rat) = Ready();
        var before = rat.Vitals.Health;

        harness.Execute(caster, "cast bolt rat");
        harness.Pump(12);

        Assert.True(
            rat.Vitals.Health < before,
            $"The rat should have been hurt; it is still on {rat.Vitals.Health}.");
    }

    [Fact]
    public void A_cast_that_hits_nothing_does_not_charge_for_it()
    {
        // Cost and cooldown are spent before the target is resolved, so a cast that resolves to
        // nothing used to bill in full and narrate "takes effect!" over the top of it.
        var (harness, caster, _) = Ready();
        var before = caster.Character.Vitals.Focus;

        harness.Execute(caster, "cast bolt nothing-by-that-name");
        harness.Pump(12);

        Assert.Equal(before, caster.Character.Vitals.Focus);
    }

    [Fact]
    public void A_cast_that_hits_nothing_says_so()
    {
        var (harness, caster, _) = Ready();
        harness.Drain(caster);

        harness.Execute(caster, "cast bolt nothing-by-that-name");
        harness.Pump(12);

        var text = harness.DrainText(caster);
        Assert.DoesNotContain("takes effect", text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_offensive_ability_with_no_named_target_falls_back_to_who_you_are_fighting()
    {
        // The common case in a real fight: you are already swinging at something, and typing the
        // mob's name again every cast is friction with no purpose.
        var (harness, caster, rat) = Ready();
        harness.Execute(caster, "kill rat");
        harness.Drain(caster);
        var before = rat.Vitals.Health;

        harness.Execute(caster, "cast bolt");
        harness.Pump(12);

        Assert.True(rat.Vitals.Health < before, "Bolt should have found the current target.");
    }

    /// <summary>
    /// A target that dies during the cast must not be billed for.
    /// </summary>
    /// <remarks>
    /// The command refuses a cast with nothing to aim at, but that check runs when the cast is
    /// *started*. Bolt takes eight pulses to land, and the rat can die inside them — at which
    /// point the ability system spends the cost and the cooldown before discovering it has
    /// nothing to apply them to.
    /// </remarks>
    [Fact]
    public void A_target_that_dies_mid_cast_does_not_cost_the_caster()
    {
        var (harness, caster, rat) = Ready();
        var before = caster.Character.Vitals.Focus;

        harness.Execute(caster, "cast bolt rat");

        // Bolt is an 8-pulse cast; the rat is gone well before it lands.
        harness.World.RemoveMob(rat);
        harness.Pump(12);

        Assert.Equal(before, caster.Character.Vitals.Focus);
    }

    [Fact]
    public void A_self_ability_needs_no_target()
    {
        var harness = Loaded();
        harness.DefineAbility("adept.shield");

        var caster = harness.AddPlayer("Mira", Room, path: CharacterPath.Adept, level: 5);
        caster.Character.Vitals.Focus = 100;
        caster.Character.Vitals.Health = 10;

        harness.Execute(caster, "cast shield");
        harness.Pump(12);

        Assert.True(caster.Character.Vitals.Health > 10, "Arcane Shield should have healed.");
    }

    [Fact]
    public void A_mob_can_be_named_by_a_word_from_its_name()
    {
        // Casting should reach a target the same way every other command does.
        var harness = Loaded();
        harness.DefineAbility("adept.bolt");

        var caster = harness.AddPlayer("Mira", Room, path: CharacterPath.Adept, level: 5);
        caster.Character.Vitals.Focus = 100;
        var rat = harness.AddMob("giant-rat", Room, name: "giant rat", health: 200);
        var before = rat.Vitals.Health;

        harness.Execute(caster, "cast bolt giant");
        harness.Pump(12);

        Assert.True(rat.Vitals.Health < before);
    }

    [Fact]
    public void Another_player_can_still_be_targeted()
    {
        // The one case that did work. It must keep working.
        var harness = Loaded();
        harness.DefineAbility("hallow.mend");

        var healer = harness.AddPlayer("Sera", Room, path: CharacterPath.Hallow, level: 5);
        healer.Character.Vitals.Focus = 100;

        var hurt = harness.AddPlayer("Kael", Room);
        hurt.Character.Vitals.Health = 10;

        harness.Execute(healer, "cast mend Kael");
        harness.Pump(12);

        Assert.True(hurt.Character.Vitals.Health > 10, "Mend should have reached Kael.");
    }
}
