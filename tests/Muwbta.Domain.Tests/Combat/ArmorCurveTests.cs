using Muwbta.Domain.Combat;

namespace Muwbta.Domain.Tests.Combat;

public class ArmorCurveTests
{
    [Fact]
    public void No_armor_absorbs_nothing()
    {
        Assert.Equal(0m, ArmorCurve.Mitigation(0, attackerLevel: 10));
    }

    [Fact]
    public void Negative_armor_absorbs_nothing_rather_than_amplifying()
    {
        Assert.Equal(0m, ArmorCurve.Mitigation(-1, attackerLevel: 10));
        Assert.Equal(0m, ArmorCurve.Mitigation(int.MinValue, attackerLevel: 10));
    }

    [Fact]
    public void Armor_matching_the_attackers_bite_absorbs_half()
    {
        // The single sentence every authored armour value is chosen against.
        Assert.Equal(0.5m, ArmorCurve.Mitigation(ArmorCurve.Bite * 20, attackerLevel: 20));
    }

    [Fact]
    public void More_armor_always_absorbs_more()
    {
        var previous = -1m;

        for (var armor = 0; armor <= 2000; armor += 10)
        {
            var mitigation = ArmorCurve.Mitigation(armor, attackerLevel: 30);
            Assert.True(mitigation >= previous, $"armour {armor} absorbed less than the rating below it");
            previous = mitigation;
        }
    }

    [Fact]
    public void Nothing_reaches_immunity()
    {
        Assert.Equal(ArmorCurve.Cap, ArmorCurve.Mitigation(int.MaxValue, attackerLevel: 1));
        Assert.Equal(ArmorCurve.Cap, ArmorCurve.Mitigation(1_000_000, attackerLevel: 50));
        Assert.True(ArmorCurve.Cap < 1m, "the cap must leave something of every blow");
    }

    /// <summary>
    /// The property that replaced the global constant.
    /// </summary>
    /// <remarks>
    /// The denominator was <c>armor + 100</c>, the same for every attacker, so an armour point was
    /// worth what it was worth forever and mitigation drifted upward tier by tier - 20% in Ossara
    /// to 60% in Nemhal across the authored realms. Measuring against the attacker makes a set
    /// worth what its author intended at their tier, and worth more against everything below it.
    /// </remarks>
    [Fact]
    public void The_same_set_absorbs_more_from_a_weaker_attacker()
    {
        var strong = ArmorCurve.Mitigation(150, attackerLevel: 40);
        var weak = ArmorCurve.Mitigation(150, attackerLevel: 20);

        Assert.True(weak > strong, $"level 20 got through {1 - weak:P0}, level 40 {1 - strong:P0}");
    }

    [Fact]
    public void A_set_authored_at_its_tier_is_worth_the_same_at_every_tier()
    {
        // Content authors a full set at roughly 3.5 x level, so that has to mean one thing.
        decimal? expected = null;

        foreach (var level in new[] { 6, 12, 18, 29, 40, 50 })
        {
            var absorbed = ArmorCurve.Mitigation((int)(3.5 * level), attackerLevel: level);

            expected ??= absorbed;
            Assert.InRange(absorbed, expected.Value - 0.02m, expected.Value + 0.02m);
        }
    }

    [Fact]
    public void A_level_zero_attacker_does_not_make_armour_absolute()
    {
        // Zero is a real case - a hand-built mob that never went through the spawner - and
        // dividing by it would hand the defender total immunity rather than throwing anywhere
        // anyone would notice.
        Assert.Equal(ArmorCurve.Mitigation(50, attackerLevel: 1), ArmorCurve.Mitigation(50, attackerLevel: 0));

        // The floor makes it a level 1 attacker rather than an infinitely weak one, and the cap
        // then holds it where it holds everything else - so the worst case is a very tough
        // defender, not an untouchable one.
        Assert.Equal(ArmorCurve.Cap, ArmorCurve.Mitigation(50, attackerLevel: 0));
    }

    [Fact]
    public void Effects_add_percentage_points_on_top()
    {
        var bare = ArmorCurve.Mitigation(ArmorCurve.Bite * 20, attackerLevel: 20);

        Assert.Equal(bare + 0.10m, ArmorCurve.Mitigation(ArmorCurve.Bite * 20, 20, 0.10m));
    }

    [Fact]
    public void Effects_cannot_push_past_the_cap()
    {
        Assert.Equal(ArmorCurve.Cap, ArmorCurve.Mitigation(ArmorCurve.Bite * 20, 20, 0.90m));
        Assert.Equal(ArmorCurve.Cap, ArmorCurve.Mitigation(10_000, 20, 0.50m));
    }

    [Fact]
    public void An_expose_cannot_drive_absorption_below_nothing()
    {
        Assert.Equal(0m, ArmorCurve.Mitigation(ArmorCurve.Bite * 20, 20, -0.90m));
        Assert.Equal(0m, ArmorCurve.Mitigation(0, 20, -0.10m));
    }
}
