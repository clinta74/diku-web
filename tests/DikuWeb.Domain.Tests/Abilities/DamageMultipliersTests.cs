using DikuWeb.Domain.Abilities.Effects;

namespace DikuWeb.Domain.Tests.Abilities;

/// <summary>
/// What the effects on two combatants do to the damage between them.
/// </summary>
/// <remarks>
/// Extracted from <c>CombatSystem</c>, where it was private and reachable only through a weapon
/// swing — which is why abilities did not honour any of it. The rule itself is unchanged; these
/// pin it so that the extraction is provable rather than assumed, and so the second caller
/// inherits a tested rule instead of a copy.
/// </remarks>
public sealed class DamageMultipliersTests
{
    private static ActiveEffect Effect(
        decimal outgoing = 1.0m,
        decimal incoming = 1.0m,
        int stacks = 1,
        EffectStackingRule rule = EffectStackingRule.Refresh) => new()
    {
        EffectKey = "test",
        Name = "tested",
        SourceEntityId = "c_test",
        OutgoingDamageMultiplier = outgoing,
        IncomingDamageMultiplier = incoming,
        Stacks = stacks,
        MaxStacks = 5,
        StackingRule = rule,
        ExpiresAtPulse = long.MaxValue,
    };

    [Fact]
    public void Nothing_running_changes_nothing()
    {
        Assert.Equal(DamageMultipliers.None, DamageMultipliers.Outgoing([]));
        Assert.Equal(DamageMultipliers.None, DamageMultipliers.Incoming([]));
        Assert.Equal(DamageMultipliers.None, DamageMultipliers.Between([], []));
    }

    /// <summary>
    /// An effect that touches neither side of the damage is not a damage effect.
    /// </summary>
    /// <remarks>
    /// Most of what is on a combatant mid-fight is a stun, a root, a defence buff or a bleed. None
    /// of them should move this, and all of them are in the list it walks.
    /// </remarks>
    [Fact]
    public void An_effect_that_is_not_about_damage_is_ignored()
    {
        Assert.Equal(DamageMultipliers.None, DamageMultipliers.Outgoing([Effect(), Effect(), Effect()]));
    }

    [Fact]
    public void A_buff_raises_what_its_bearer_deals()
    {
        Assert.Equal(1.6m, DamageMultipliers.Outgoing([Effect(outgoing: 1.6m)]));
    }

    [Fact]
    public void A_debuff_raises_what_its_bearer_takes()
    {
        Assert.Equal(1.3m, DamageMultipliers.Incoming([Effect(incoming: 1.3m)]));
    }

    /// <summary>
    /// The two sides compose, so a fury and a curse are worth both rather than whichever landed last.
    /// </summary>
    [Fact]
    public void The_attackers_buff_and_the_targets_debuff_multiply()
    {
        // Arcane Surge into Sunder, which is the real pair this was found through.
        Assert.Equal(
            2.08m,
            DamageMultipliers.Between([Effect(outgoing: 1.6m)], [Effect(incoming: 1.3m)]));
    }

    /// <summary>Two buffs on one side compose the same way, and in either order.</summary>
    [Fact]
    public void Order_cannot_matter()
    {
        var a = Effect(outgoing: 1.25m);
        var b = Effect(outgoing: 1.3m);

        Assert.Equal(DamageMultipliers.Outgoing([a, b]), DamageMultipliers.Outgoing([b, a]));
    }

    /// <summary>
    /// A stacking effect scales its bonus, not itself.
    /// </summary>
    /// <remarks>
    /// Three stacks of a 1.2 is 1.6 and not 1.728. Compounding a stack against itself is how a
    /// debuff meant to be worth twenty percent becomes worth seventy at five stacks.
    /// </remarks>
    [Fact]
    public void Stacks_add_the_bonus_rather_than_compounding_it()
    {
        var stacked = Effect(incoming: 1.2m, stacks: 3, rule: EffectStackingRule.Stack);

        Assert.Equal(1.6m, DamageMultipliers.Incoming([stacked]));
    }

    /// <summary>And a refreshing effect is worth one application however often it was re-cast.</summary>
    [Fact]
    public void Refresh_holds_one_application_whatever_the_stack_count_says()
    {
        var refreshed = Effect(incoming: 1.2m, stacks: 3, rule: EffectStackingRule.Refresh);

        Assert.Equal(1.2m, DamageMultipliers.Incoming([refreshed]));
    }

    // -----------------------------------------------------------------------
    // Turning a multiplier into damage
    // -----------------------------------------------------------------------

    [Fact]
    public void An_unmodified_number_is_returned_exactly()
    {
        Assert.Equal(17, DamageMultipliers.Apply(17, DamageMultipliers.None));
    }

    /// <summary>
    /// Rounded away from zero, so a small hit under a small buff moves at all.
    /// </summary>
    /// <remarks>
    /// The tiny numbers are where a player is most likely to conclude a buff does nothing, and
    /// banker's rounding sends 1 × 1.5 back to 2 but 2 × 1.25 back to 2 as well.
    /// </remarks>
    [Theory]
    [InlineData(1, 1.5, 2)]
    [InlineData(2, 1.25, 3)]
    [InlineData(24, 1.6, 38)]
    [InlineData(10, 0.75, 8)]
    public void A_multiplier_rounds_away_from_zero(int damage, double multiplier, int expected)
    {
        Assert.Equal(expected, DamageMultipliers.Apply(damage, (decimal)multiplier));
    }

    /// <summary>
    /// Damage never inverts. A debuff strong enough to go negative would otherwise heal.
    /// </summary>
    [Fact]
    public void A_multiplier_cannot_turn_damage_into_healing()
    {
        Assert.Equal(0, DamageMultipliers.Apply(10, -2.0m));
    }
}
