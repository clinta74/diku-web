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

    // -----------------------------------------------------------------------
    // Names that are already whole
    // -----------------------------------------------------------------------

    /// <summary>
    /// A name that heads itself takes no article.
    /// </summary>
    /// <remarks>
    /// <b>Reported from a combat log, where it appeared about forty times in one fight.</b> The
    /// Reaches names its unquiet dead "one of the owed", "one of the long held", "someone who has
    /// been waiting" — noun phrases with a pronoun at the head. Prefixing an article gave
    /// <em>"an one of the owed"</em>: once per swing, once per miss, and again when it fell.
    /// </remarks>
    [Theory]
    [InlineData("one of the owed")]
    [InlineData("one of the left behind")]
    [InlineData("one of the long held")]
    [InlineData("one of the recognised")]
    [InlineData("one of the untold")]
    [InlineData("someone who has been waiting")]
    [InlineData("somebody's keepsake")]
    [InlineData("something carried in")]
    public void A_name_that_heads_itself_takes_no_article(string name)
    {
        Assert.Equal(name, NarrationHelper.WithArticle(name));
    }

    /// <summary>
    /// And the definite form keeps every word, rather than swapping the first one out.
    /// </summary>
    /// <remarks>
    /// This is why the two predicates are separate. An article can be replaced — "a rat" drops its
    /// first word to become "the rat" — and a pronoun cannot: the same move on "one of the owed"
    /// leaves "the of the owed".
    /// </remarks>
    [Fact]
    public void A_pronoun_led_name_keeps_all_of_its_words()
    {
        Assert.Equal("one of the owed", NarrationHelper.WithDefiniteArticle("one of the owed"));
        Assert.Equal("One of the owed", NarrationHelper.WithDefiniteArticle("one of the owed", capitalize: true));
    }

    /// <summary>Capitalised for the start of a sentence, which is where it is usually read.</summary>
    [Fact]
    public void A_whole_name_still_capitalizes_when_it_opens_a_sentence()
    {
        Assert.Equal("One of the owed", NarrationHelper.WithArticle("one of the owed", capitalize: true));
        Assert.Equal(
            "One of the owed falls.",
            NarrationHelper.BuildSentence("one of the owed", "falls"));
    }

    /// <summary>
    /// The pronouns are matched as whole words, so a name that merely begins with those letters
    /// is still an ordinary noun.
    /// </summary>
    [Theory]
    [InlineData("oneiric mask", "an oneiric mask")]
    [InlineData("somatic charm", "a somatic charm")]
    public void A_word_that_only_starts_like_a_pronoun_is_not_one(string name, string expected)
    {
        Assert.Equal(expected, NarrationHelper.WithArticle(name));
    }

    /// <summary>
    /// The article follows the sound, not the spelling.
    /// </summary>
    /// <remarks>
    /// English keeps these in a short list and both directions occur: a leading "u" that says
    /// "you" takes "a", and a silent "h" takes "an". The one that turned up in play was "one" —
    /// a vowel on the page and a consonant in the mouth — which is how a bare "one" would have
    /// gone on reading "an one" even after pronoun-led phrases stopped taking an article at all.
    /// </remarks>
    [Theory]
    [InlineData("one", "a one")]
    [InlineData("one-eyed dog", "a one-eyed dog")]
    [InlineData("unicorn horn", "a unicorn horn")]
    [InlineData("used blade", "a used blade")]
    [InlineData("hour candle", "an hour candle")]
    [InlineData("honest mistake", "an honest mistake")]
    [InlineData("orc", "an orc")]
    [InlineData("hammer", "a hammer")]
    // "oneiric" opens with the same three letters as "one" and is said "oh-", which is why the
    // consonant-sounding list holds whole words where it can and stems only where every word
    // built on them sounds alike.
    [InlineData("oneiric mask", "an oneiric mask")]
    [InlineData("unclaimed thing", "an unclaimed thing")]
    public void The_article_follows_the_sound(string name, string expected)
    {
        Assert.Equal(expected, NarrationHelper.WithArticle(name));
    }

    /// <summary>
    /// A plural is authored with a phrase that carries its own article, rather than detected.
    /// </summary>
    /// <remarks>
    /// No string can tell a plural from "harness" or "grass", so the epic wraps are named "a pair
    /// of quiet wraps" — which the article check already handles, and which is what somebody would
    /// say out loud anyway. They were "unproven quiet wraps" for exactly one commit, and read as
    /// "an unproven quiet wraps".
    /// </remarks>
    [Fact]
    public void A_plural_named_as_a_pair_reads_correctly()
    {
        Assert.Equal(
            "a pair of unproven quiet wraps",
            NarrationHelper.WithArticle("a pair of unproven quiet wraps"));

        Assert.Equal(
            "the pair of unproven quiet wraps",
            NarrationHelper.WithDefiniteArticle("a pair of unproven quiet wraps"));
    }
}
