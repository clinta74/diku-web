using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Inhabitants;

namespace DikuWeb.Domain.Tests.Combat;

/// <summary>
/// One place decides what a mob's attacks are, so silence means the same thing everywhere and
/// an empty list in the database keeps meaning "empty" rather than being rewritten on save.
/// </summary>
public sealed class MobAttackResolverTests
{
    [Fact]
    public void A_template_with_no_attacks_gets_the_pre_existing_one()
    {
        var attack = Assert.Single(MobAttackResolver.Resolve(Template()));

        Assert.Equal(AttackTiming.DefaultVerb, attack.Verb);
        Assert.Equal(AttackTiming.DefaultDelayPulses, attack.DelayPulses);
        Assert.Null(attack.DamageMultiplier);
    }

    [Fact]
    public void A_missing_template_still_yields_an_attack()
    {
        // A mob whose template was deleted mid-fight must keep swinging rather than freeze.
        Assert.Single(MobAttackResolver.Resolve(null));
    }

    [Fact]
    public void Authored_attacks_are_kept_in_order()
    {
        var attacks = MobAttackResolver.Resolve(Template(
            new MobAttack { Verb = "bite", DelayPulses = 4 },
            new MobAttack { Verb = "claw", DelayPulses = 6, DamageMultiplier = 0.5m }));

        Assert.Equal(2, attacks.Count);
        Assert.Equal("bite", attacks[0].Verb);
        Assert.Equal("claw", attacks[1].Verb);
        Assert.Equal(0.5m, attacks[1].DamageMultiplier);
    }

    [Fact]
    public void A_delay_below_the_floor_is_clamped_rather_than_honoured()
    {
        // Validation refuses these on save; a row that predates the rule, or one written by
        // hand, must not be allowed to outrun the floor at runtime either.
        var attack = Assert.Single(MobAttackResolver.Resolve(Template(
            new MobAttack { Verb = "bite", DelayPulses = 1 })));

        Assert.Equal(AttackTiming.MinDelayPulses, attack.DelayPulses);
    }

    [Fact]
    public void A_blank_verb_falls_back_rather_than_narrating_nothing()
    {
        var attack = Assert.Single(MobAttackResolver.Resolve(Template(
            new MobAttack { Verb = "  ", DelayPulses = 8 })));

        Assert.Equal(AttackTiming.DefaultVerb, attack.Verb);
    }

    [Fact]
    public void A_nonsense_multiplier_is_dropped()
    {
        var attack = Assert.Single(MobAttackResolver.Resolve(Template(
            new MobAttack { Verb = "bite", DelayPulses = 8, DamageMultiplier = 0m })));

        Assert.Null(attack.DamageMultiplier);
    }

    private static MobTemplate Template(params MobAttack[] attacks) => new()
    {
        Key = "rat",
        Name = "a rat",
        Icon = "r",
        Attacks = [.. attacks],
    };
}
