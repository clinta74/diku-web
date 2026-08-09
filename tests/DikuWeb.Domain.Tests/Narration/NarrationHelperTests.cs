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
    public void WithArticle_leaves_a_proper_name_alone()
    {
        // A capitalized template name is the builder saying "this is a name, not a kind of
        // thing". "A Grimble hits you" would be wrong in every position.
        Assert.Equal("Grimble", NarrationHelper.WithArticle("Grimble"));
        Assert.Equal("Grimble", NarrationHelper.WithArticle("Grimble", capitalize: true));
        Assert.Equal("Excalibur", NarrationHelper.WithArticle("Excalibur"));
    }

    [Theory]
    [InlineData("Grimble", true)]
    [InlineData("Excalibur", true)]
    [InlineData("large rat", false)]
    [InlineData("long sword", false)]
    [InlineData("", false)]
    public void IsProperName_reads_the_builders_capitalization(string name, bool expected) =>
        Assert.Equal(expected, NarrationHelper.IsProperName(name));

    [Fact]
    public void WithDefiniteArticle_names_an_established_thing()
    {
        Assert.Equal("the long sword", NarrationHelper.WithDefiniteArticle("long sword"));
        Assert.Equal("The long sword", NarrationHelper.WithDefiniteArticle("long sword", capitalize: true));
    }

    [Fact]
    public void WithDefiniteArticle_leaves_a_proper_name_alone()
    {
        // "You drop the Excalibur." is not English.
        Assert.Equal("Excalibur", NarrationHelper.WithDefiniteArticle("Excalibur"));
        Assert.Equal("Excalibur", NarrationHelper.WithDefiniteArticle("Excalibur", capitalize: true));
        Assert.Equal("You drop Excalibur.", $"You drop {NarrationHelper.WithDefiniteArticle("Excalibur")}.");
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
    public void BuildSentence_gives_a_proper_name_no_article()
    {
        Assert.Equal("Grimble is here.", NarrationHelper.BuildSentence("Grimble", "is here."));
        Assert.Equal("Grimble leaves north.", NarrationHelper.BuildSentence("Grimble", "leaves north"));
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

    // -----------------------------------------------------------------------
    // A name that already carries its article
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("a rat", "a rat")]
    [InlineData("an orc", "an orc")]
    [InlineData("the innkeeper", "the innkeeper")]
    public void WithArticle_leaves_a_name_that_already_has_one_alone(string name, string expected)
    {
        // Templates are authored by hand, and "a rat" is at least as natural to type as "rat".
        // Without this the builder's own wording decided whether the game said "an a rat" - in
        // combat, in the room listing, and in every ability that names a target.
        Assert.Equal(expected, NarrationHelper.WithArticle(name));
    }

    [Fact]
    public void WithArticle_still_capitalizes_a_name_that_brought_its_own_article()
    {
        Assert.Equal("A rat", NarrationHelper.WithArticle("a rat", capitalize: true));
    }

    [Theory]
    [InlineData("a rat", "the rat")]
    [InlineData("an orc", "the orc")]
    [InlineData("the innkeeper", "the innkeeper")]
    public void WithDefiniteArticle_swaps_the_article_rather_than_stacking_them(
        string name,
        string expected)
    {
        // What the builder wrote is a noun phrase, not a bare noun, so the indefinite article is
        // replaced rather than prefixed.
        Assert.Equal(expected, NarrationHelper.WithDefiniteArticle(name));
    }

    [Fact]
    public void A_bare_noun_is_unaffected_by_any_of_this()
    {
        // The common case, and the one every existing call site relies on.
        Assert.Equal("a rat", NarrationHelper.WithArticle("rat"));
        Assert.Equal("the rat", NarrationHelper.WithDefiniteArticle("rat"));
    }
}
