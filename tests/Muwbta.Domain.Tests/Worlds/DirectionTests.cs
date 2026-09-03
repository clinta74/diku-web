using Muwbta.Domain.Worlds;

namespace Muwbta.Domain.Tests.Worlds;

public sealed class DirectionTests
{
    [Theory]
    [InlineData(Direction.North, Direction.South)]
    [InlineData(Direction.South, Direction.North)]
    [InlineData(Direction.East, Direction.West)]
    [InlineData(Direction.West, Direction.East)]
    [InlineData(Direction.Up, Direction.Down)]
    [InlineData(Direction.Down, Direction.Up)]
    public void Opposite_pairs_correctly(Direction direction, Direction expected) =>
        Assert.Equal(expected, direction.Opposite());

    [Fact]
    public void Opposite_is_an_involution_for_every_direction()
    {
        // dig creates reciprocal exits by default (PLAN.md §7.6), so an asymmetric
        // Opposite() would silently produce one-way passages all over the world.
        foreach (var direction in DirectionExtensions.All)
        {
            Assert.Equal(direction, direction.Opposite().Opposite());
        }
    }

    [Theory]
    [InlineData("n", Direction.North)]
    [InlineData("no", Direction.North)]
    [InlineData("north", Direction.North)]
    [InlineData("NORTH", Direction.North)]
    [InlineData("  e  ", Direction.East)]
    [InlineData("s", Direction.South)]
    [InlineData("w", Direction.West)]
    [InlineData("u", Direction.Up)]
    [InlineData("d", Direction.Down)]
    public void TryParse_accepts_names_and_prefixes(string input, Direction expected)
    {
        Assert.True(DirectionExtensions.TryParse(input, out var direction));
        Assert.Equal(expected, direction);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("x")]
    [InlineData("northeast")]
    public void TryParse_rejects_unknown_input(string input) =>
        Assert.False(DirectionExtensions.TryParse(input, out _));

    [Fact]
    public void TryParse_rejects_null() =>
        Assert.False(DirectionExtensions.TryParse(null, out _));

    [Fact]
    public void Every_direction_has_a_unique_abbreviation()
    {
        // Single letters are what players actually type. A collision would make one
        // direction unreachable by its shortcut.
        var abbreviations = DirectionExtensions.All
            .Select(d => d.Abbreviation())
            .ToList();

        Assert.Equal(abbreviations.Count, abbreviations.Distinct().Count());
    }

    [Fact]
    public void All_covers_every_enum_value() =>
        Assert.Equal(
            Enum.GetValues<Direction>().Length,
            DirectionExtensions.All.Distinct().Count());
}
