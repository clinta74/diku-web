using Muwbta.Domain.Characters;

namespace Muwbta.Domain.Tests.Characters;

public sealed class AttributeSetTests
{
    [Theory]
    [InlineData(10, 0)]
    [InlineData(11, 0)]
    [InlineData(12, 1)]
    [InlineData(20, 5)]
    [InlineData(1, -5)]
    public void ModifierFor_matches_the_published_formula(int value, int expected) =>
        Assert.Equal(expected, AttributeSet.ModifierFor(value));

    [Theory]
    [InlineData(9, -1)]
    [InlineData(8, -1)]
    [InlineData(7, -2)]
    [InlineData(6, -2)]
    [InlineData(5, -3)]
    public void ModifierFor_rounds_down_for_below_average_attributes(int value, int expected)
    {
        // Guards a real trap: C# integer division truncates toward zero, so (7 - 10) / 2
        // yields -1 where PLAN.md §4.5 requires rounding down to -2. Getting this wrong
        // would quietly make every weak character stronger than designed, and combat
        // reads these modifiers on every roll.
        Assert.Equal(expected, AttributeSet.ModifierFor(value));
    }

    [Fact]
    public void Baseline_has_a_zero_modifier_across_the_board()
    {
        var attributes = AttributeSet.Baseline;

        Assert.Equal(0, attributes.MightModifier);
        Assert.Equal(0, attributes.AgilityModifier);
        Assert.Equal(0, attributes.VitalityModifier);
        Assert.Equal(0, attributes.InsightModifier);
        Assert.Equal(0, attributes.ResolveModifier);
    }

    [Fact]
    public void Modifier_is_monotonic_across_the_whole_legal_range()
    {
        var previous = AttributeSet.ModifierFor(AttributeSet.MinValue);

        for (var value = AttributeSet.MinValue + 1; value <= AttributeSet.MaxValue; value++)
        {
            var current = AttributeSet.ModifierFor(value);
            Assert.True(
                current >= previous,
                $"Modifier decreased going from {value - 1} to {value}: {previous} -> {current}");
            previous = current;
        }
    }
}
