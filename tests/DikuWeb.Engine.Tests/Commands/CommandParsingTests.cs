using DikuWeb.Engine.Commands;

namespace DikuWeb.Engine.Tests.Commands;

public sealed class CommandParsingTests
{
    private readonly CommandRegistry _registry = new();

    [Theory]
    [InlineData("look", "look", "")]
    [InlineData("  look  ", "look", "")]
    [InlineData("say hello", "say", "hello")]
    [InlineData("LOOK", "look", "")]
    public void Split_separates_verb_from_argument(string input, string verb, string argument)
    {
        var (actualVerb, actualArgument) = CommandRegistry.Split(input);

        Assert.Equal(verb, actualVerb);
        Assert.Equal(argument, actualArgument);
    }

    [Fact]
    public void Split_preserves_spacing_inside_the_argument()
    {
        // "say  hello   there" must reach the room intact; only the separator after the
        // verb is consumed, or players cannot control their own punctuation and spacing.
        var (verb, argument) = CommandRegistry.Split("say  hello   there");

        Assert.Equal("say", verb);
        Assert.Equal("hello   there", argument);
    }

    [Theory]
    [InlineData("n", "north")]
    [InlineData("no", "north")]
    [InlineData("north", "north")]
    [InlineData("e", "east")]
    [InlineData("s", "south")]
    [InlineData("w", "west")]
    [InlineData("u", "up")]
    [InlineData("d", "down")]
    [InlineData("l", "look")]
    [InlineData("lo", "look")]
    [InlineData("say", "say")]
    [InlineData("h", "help")]
    public void Abbreviations_resolve_to_the_expected_command(string typed, string expected)
    {
        var command = _registry.Find(typed);

        Assert.NotNull(command);
        Assert.Equal(expected, command.Name);
    }

    [Fact]
    public void Single_letter_directions_beat_other_verbs()
    {
        // Directions are the most-typed input in the game, so they sit first in the table.
        // If "s" ever resolved to "say" instead of "south", movement would break constantly.
        Assert.Equal("south", _registry.Find("s")?.Name);
        Assert.Equal("west", _registry.Find("w")?.Name);
    }

    [Theory]
    [InlineData("q")]
    [InlineData("qu")]
    [InlineData("qui")]
    public void Quit_refuses_to_match_a_partial_word(string typed)
    {
        // Losing a character to a fumbled keypress is a bad experience, so quit demands
        // all four letters even though every other verb accepts a prefix.
        Assert.Null(_registry.Find(typed));
    }

    [Fact]
    public void Quit_matches_when_fully_typed() =>
        Assert.Equal("quit", _registry.Find("quit")?.Name);

    [Theory]
    [InlineData("")]
    [InlineData("xyzzy")]
    [InlineData("lookout")]  // longer than the verb, so not an abbreviation of it
    public void Unknown_verbs_do_not_match(string typed) =>
        Assert.Null(_registry.Find(typed));

    [Fact]
    public void Every_command_advertises_help_text() =>
        Assert.All(_registry.Commands, c => Assert.False(string.IsNullOrWhiteSpace(c.Help)));
}
