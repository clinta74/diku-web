using DikuWeb.Domain.Combat;

namespace DikuWeb.Domain.Tests.Combat;

/// <summary>
/// The curve exists to make two promises unbreakable by content: armour always helps, and armour
/// never finishes the job. Both used to depend on authored numbers being sensible.
/// </summary>
public sealed class ArmorCurveTests
{
    [Fact]
    public void No_armor_absorbs_nothing()
    {
        Assert.Equal(0m, ArmorCurve.Mitigation(0));
    }

    [Fact]
    public void Negative_armor_absorbs_nothing_rather_than_amplifying()
    {
        // A subtraction would have turned a negative into a bonus for the attacker.
        Assert.Equal(0m, ArmorCurve.Mitigation(-1));
        Assert.Equal(0m, ArmorCurve.Mitigation(int.MinValue));
    }

    [Fact]
    public void The_midpoint_absorbs_exactly_half()
    {
        // The one sentence every item value in the game is chosen against.
        Assert.Equal(0.5m, ArmorCurve.Mitigation(ArmorCurve.Midpoint));
    }

    [Fact]
    public void More_armor_always_absorbs_more_until_the_cap()
    {
        var previous = -1m;

        for (var armor = 0; armor <= 300; armor += 5)
        {
            var mitigation = ArmorCurve.Mitigation(armor);
            Assert.True(mitigation >= previous, $"armour {armor} absorbed less than {armor - 5}");
            previous = mitigation;
        }
    }

    [Fact]
    public void Nothing_reaches_immunity_however_absurd_the_rating()
    {
        // The property flat subtraction could never have. A builder's mistyped extra zero must not
        // produce a character nothing can hurt.
        Assert.Equal(ArmorCurve.Cap, ArmorCurve.Mitigation(int.MaxValue));
        Assert.Equal(ArmorCurve.Cap, ArmorCurve.Mitigation(1_000_000));
        Assert.True(ArmorCurve.Cap < 1m, "the cap must leave something of every blow");
    }

    [Fact]
    public void Effects_move_the_fraction_and_are_clamped_with_it()
    {
        // Guards carry percentage points so a shout is worth the same at every tier. They are
        // summed into the gear's fraction and clamped once, so a stack of them cannot exceed the
        // ceiling gear alone respects.
        Assert.Equal(0.6m, ArmorCurve.Mitigation(ArmorCurve.Midpoint, 0.10m));
        Assert.Equal(ArmorCurve.Cap, ArmorCurve.Mitigation(ArmorCurve.Midpoint, 0.90m));
        Assert.Equal(ArmorCurve.Cap, ArmorCurve.Mitigation(10_000, 0.50m));
    }

    [Fact]
    public void An_expose_can_strip_armour_but_never_past_defenceless()
    {
        // Negative deltas are how debuff.expose works. Driving the total below zero lands on zero,
        // which is the worst any defender can be - not a bonus to whoever hit them.
        Assert.Equal(0m, ArmorCurve.Mitigation(ArmorCurve.Midpoint, -0.90m));
        Assert.Equal(0m, ArmorCurve.Mitigation(0, -0.10m));
    }

    [Theory]
    [InlineData(25, 0.20)]    // Ossara
    [InlineData(55, 0.35)]    // Grask
    [InlineData(95, 0.49)]    // Azhen
    [InlineData(150, 0.60)]   // Nemhal
    [InlineData(210, 0.68)]   // the Unlit
    public void The_realm_set_totals_land_where_the_world_bible_says(int armor, double expected)
    {
        // WORLD.md §7.3 picks one armour total per realm and derives every piece from it. If this
        // drifts, the item spine and the design document have stopped agreeing.
        Assert.Equal((decimal)expected, ArmorCurve.Mitigation(armor), precision: 2);
    }
}
