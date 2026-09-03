using Muwbta.Domain.Characters;

namespace Muwbta.Domain.Tests.Characters;

/// <summary>
/// Hunger and thirst are an upkeep, and the tests are mostly about what they refuse to do.
/// </summary>
public sealed class NeedsTests
{
    /// <summary>
    /// A fed character recovers at exactly the rate they did before any of this existed.
    /// </summary>
    /// <remarks>
    /// The guard on the whole feature. Every regen number in the game was tuned against a
    /// multiplier of one, so a fed character has to still get one — otherwise this quietly retunes
    /// recovery for everybody who was never hungry in the first place.
    /// </remarks>
    [Fact]
    public void Being_fed_costs_nothing()
    {
        Assert.Equal(1.0, Needs.RegenShare(0, 0));
    }

    /// <summary>It slows recovery and never stops it.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    [InlineData(50)]
    [InlineData(100)]
    public void Recovery_slows_but_never_stops(int need)
    {
        var share = Needs.RegenShare(need, need);

        Assert.InRange(share, Needs.SlowestRegenShare, 1.0);
    }

    /// <summary>At its worst it is the documented floor, not zero and not something near it.</summary>
    [Fact]
    public void The_worst_it_gets_is_the_floor()
    {
        Assert.Equal(Needs.SlowestRegenShare, Needs.RegenShare(Needs.Worst, Needs.Worst), 3);
    }

    /// <summary>
    /// The worse of the two decides it, so neglecting both is not punished twice.
    /// </summary>
    [Fact]
    public void The_worse_need_decides_it()
    {
        Assert.Equal(Needs.RegenShare(80, 0), Needs.RegenShare(80, 80), 6);
        Assert.Equal(Needs.RegenShare(0, 80), Needs.RegenShare(80, 80), 6);
    }

    /// <summary>Nothing outside the scale, however it is asked.</summary>
    [Theory]
    [InlineData(-50, 0)]
    [InlineData(500, 0)]
    [InlineData(0, -50)]
    [InlineData(0, 500)]
    public void Absurd_values_stay_on_the_scale(int hunger, int thirst)
    {
        Assert.InRange(Needs.RegenShare(hunger, thirst), Needs.SlowestRegenShare, 1.0);
    }

    /// <summary>A need says nothing until it is worth saying.</summary>
    [Fact]
    public void A_mild_need_is_silent()
    {
        Assert.Null(Needs.DescribeHunger(0));
        Assert.Null(Needs.DescribeThirst(Needs.Thresholds[0] - 1));

        Assert.NotNull(Needs.DescribeHunger(Needs.Thresholds[0]));
        Assert.NotNull(Needs.DescribeThirst(Needs.Worst));
    }

    /// <summary>Eating never takes a character past full, and never below it.</summary>
    [Theory]
    [InlineData(10, 40, 0)]
    [InlineData(100, 40, 60)]
    [InlineData(0, 40, 0)]
    [InlineData(50, -5, 50)]
    public void Answering_a_need_lands_on_the_scale(int current, int by, int expected)
    {
        Assert.Equal(expected, Needs.Reduced(current, by));
    }

    /// <summary>And it never grows past its worst, however long nobody eats.</summary>
    [Fact]
    public void A_need_stops_at_its_worst()
    {
        Assert.Equal(Needs.Worst, Needs.Increased(Needs.Worst, 10));
        Assert.Equal(Needs.Worst, Needs.Increased(Needs.Worst - 1, 5));
    }

    /// <summary>
    /// The penalty reaches the numbers the regeneration tick actually uses.
    /// </summary>
    /// <remarks>
    /// <c>Needs.RegenShare</c> being right is worth nothing if nothing multiplies by it, which is
    /// the failure this repo keeps finding: a field that is authored, described, and read by
    /// nobody.
    /// </remarks>
    [Fact]
    public void A_starving_character_regenerates_less_than_a_fed_one()
    {
        var fed = Vitals.StartingFor(CharacterPath.Warden);
        var starving = Vitals.StartingFor(CharacterPath.Warden);
        starving.Hunger = Needs.Worst;

        var fedRates = RegenCalculator.Calculate(CharacterRestState.Rest, fed, 0, CharacterPath.Warden);
        var starvingRates = RegenCalculator.Calculate(CharacterRestState.Rest, starving, 0, CharacterPath.Warden);

        Assert.True(starvingRates.health < fedRates.health);
        Assert.True(starvingRates.stamina < fedRates.stamina);
    }
}
