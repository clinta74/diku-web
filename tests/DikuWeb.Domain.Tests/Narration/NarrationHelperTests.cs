using DikuWeb.Domain.Narration;

namespace DikuWeb.Domain.Tests.Narration;

public sealed class NarrationHelperTests
{
    [Theory]
    [InlineData("rat", "a rat")]
    [InlineData("long sword", "a long sword")]
    [InlineData("orc", "an orc")]
    [InlineData("iron key", "an iron key")]
    [InlineData("elderly merchant", "an elderly merchant")]
    public void WithArticle_picks_a_or_an_from_the_first_letter(string name, string expected) =>
        Assert.Equal(expected, NarrationHelper.WithArticle(name));

    [Fact]
    public void WithArticle_stays_lower_case_by_default()
    {
        // The bug this guards: "You see A long sword." A noun phrase dropped into the middle
        // of a sentence must not carry a capital, and mid-sentence is the common case.
        Assert.Equal("a long sword", NarrationHelper.WithArticle("long sword"));
        Assert.Equal("You see a long sword.", $"You see {NarrationHelper.WithArticle("long sword")}.");
    }

    [Fact]
    public void WithArticle_capitalizes_only_when_asked()
    {
        Assert.Equal("A large rat", NarrationHelper.WithArticle("large rat", capitalize: true));
        Assert.Equal("An orc", NarrationHelper.WithArticle("orc", capitalize: true));
    }

    [Fact]
    public void WithArticle_leaves_the_rest_of_the_name_alone()
    {
        // Only the first character is touched, so an intentional capital survives.
        Assert.Equal("a Millbrook guard", NarrationHelper.WithArticle("Millbrook guard"));
        Assert.Equal("A Millbrook guard", NarrationHelper.WithArticle("Millbrook guard", capitalize: true));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void WithArticle_passes_empty_names_through(string? name) =>
        Assert.Equal(name, NarrationHelper.WithArticle(name!));

    [Fact]
    public void BuildSentence_produces_a_capital_and_a_full_stop()
    {
        Assert.Equal("A rat is here.", NarrationHelper.BuildSentence("rat", "is here."));
        Assert.Equal("An orc appears.", NarrationHelper.BuildSentence("orc", "appears."));
    }

    [Fact]
    public void BuildSentence_terminates_a_predicate_that_did_not()
    {
        // Mob wandering passes "leaves north" with no punctuation; the sentence still needs it.
        Assert.Equal("A large rat leaves north.", NarrationHelper.BuildSentence("large rat", "leaves north"));
        Assert.Equal(
            "A large rat arrives from the south.",
            NarrationHelper.BuildSentence("large rat", "arrives from the south"));
    }

    [Theory]
    [InlineData("is here.", "A rat is here.")]
    [InlineData("flees!", "A rat flees!")]
    [InlineData("goes where?", "A rat goes where?")]
    public void BuildSentence_does_not_double_up_terminal_punctuation(string predicate, string expected) =>
        Assert.Equal(expected, NarrationHelper.BuildSentence("rat", predicate));

    [Fact]
    public void Capitalize_leaves_an_already_capital_first_letter_alone() =>
        Assert.Equal("Alice waves.", NarrationHelper.Capitalize("Alice waves."));

    [Fact]
    public void FormatProse_articles_entity_tokens_and_capitalizes_the_line()
    {
        var result = NarrationHelper.FormatProse(
            "{entity:mob} blocks {player:name}.",
            new Dictionary<string, string> { ["mob"] = "orc", ["name"] = "Alice" });

        Assert.Equal("An orc blocks Alice.", result);
    }

    [Fact]
    public void FormatProse_leaves_unknown_tokens_untouched()
    {
        var result = NarrationHelper.FormatProse("{entity:mob} eyes {missing}.",
            new Dictionary<string, string> { ["mob"] = "rat" });

        Assert.Equal("A rat eyes {missing}.", result);
    }
}
