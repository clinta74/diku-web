using Muwbta.Engine.Commands;

namespace Muwbta.Engine.Tests.Commands;

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

    /// <summary>
    /// Punctuation shortcuts expand with no space after them.
    /// </summary>
    /// <remarks>
    /// Expanded in <c>Split</c> rather than registered as verbs, because a
    /// <c>CommandDefinition</c> matches by prefix and so would require the separator - typing
    /// <c>'hello</c> is the whole point, and <c>' hello</c> is what a registered verb would have
    /// demanded.
    /// </remarks>
    [Theory]
    [InlineData("'hello there", "say", "hello there")]
    [InlineData("\"hello there", "say", "hello there")]
    [InlineData(";grins slowly", "emote", "grins slowly")]
    [InlineData(":grins slowly", "emote", "grins slowly")]
    public void Punctuation_shortcuts_expand_to_a_verb(string typed, string verb, string argument)
    {
        var (parsedVerb, parsedArgument) = CommandRegistry.Split(typed);

        Assert.Equal(verb, parsedVerb);
        Assert.Equal(argument, parsedArgument);
    }

    [Theory]
    [InlineData("' hello", "hello")]
    [InlineData("'   hello  ", "hello")]
    public void A_shortcut_tolerates_a_space_after_it(string typed, string argument) =>
        Assert.Equal(argument, CommandRegistry.Split(typed).Argument);

    [Fact]
    public void A_bare_shortcut_carries_no_argument()
    {
        // Falls through to the verb's own "Say what?" rather than saying an empty string.
        var (verb, argument) = CommandRegistry.Split("'");

        Assert.Equal("say", verb);
        Assert.Equal(string.Empty, argument);
    }

    [Fact]
    public void A_shortcut_keeps_the_case_of_what_follows()
    {
        // Verbs are lowercased; a message must not be. "'Hello" is a greeting, not a shout.
        Assert.Equal("Hello There", CommandRegistry.Split("'Hello There").Argument);
    }

    [Theory]
    [InlineData("say hello", "say", "hello")]
    [InlineData("look", "look", "")]
    public void Ordinary_input_is_unaffected(string typed, string verb, string argument)
    {
        var parsed = CommandRegistry.Split(typed);

        Assert.Equal(verb, parsed.Verb);
        Assert.Equal(argument, parsed.Argument);
    }

    [Fact]
    public void Every_shortcut_expands_to_a_verb_that_exists()
    {
        // A shortcut pointing at a verb nobody registered would report "not something you can do"
        // for a key the help text advertises.
        foreach (var typed in new[] { "'x", "\"x", ";x", ":x" })
        {
            var verb = CommandRegistry.Split(typed).Verb;
            Assert.NotNull(_registry.Find(verb));
        }
    }
}
