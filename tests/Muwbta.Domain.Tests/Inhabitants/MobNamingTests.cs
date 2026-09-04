using Muwbta.Domain.Inhabitants;

namespace Muwbta.Domain.Tests.Inhabitants;

/// <summary>
/// Where a spawner's one word goes in a template's name, and which names refuse one
/// (PLAN.md §4.8).
/// </summary>
/// <remarks>
/// The article is the whole difficulty. A name is authored with one ("a rat", "an engine"), the
/// word goes <em>after</em> it, and the article that was right for the noun is not necessarily
/// right for the word — "an hall engine" is the line this class exists to never print.
/// </remarks>
public sealed class MobNamingTests
{
    [Theory]
    [InlineData("a rat", "wharf", "a wharf rat")]
    [InlineData("a brigand", "marsh", "a marsh brigand")]
    [InlineData("an ox", "old", "an old ox")]
    [InlineData("an engine", "hall", "a hall engine")]
    [InlineData("a rat", "old", "an old rat")]
    [InlineData("A Rat", "wharf", "A Rat")]
    public void The_word_goes_after_the_article_and_the_article_is_re_picked(
        string name, string modifier, string expected)
    {
        Assert.Equal(expected, MobNaming.Apply(name, modifier));
    }

    [Fact]
    public void A_definite_article_is_kept()
    {
        // "the kept" is a kind, not a person: lower-case after the article.
        Assert.Equal("the deep kept", MobNaming.Apply("the kept", "deep"));
    }

    [Fact]
    public void A_bare_name_is_prefixed_and_gets_its_article_later()
    {
        // WithArticle does the rest at narration time, as it did before modifiers existed.
        Assert.Equal("wharf rat", MobNaming.Apply("rat", "wharf"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_modifier_is_the_template_name_unchanged(string? modifier)
    {
        Assert.Equal("a rat", MobNaming.Apply("a rat", modifier));
    }

    [Fact]
    public void The_modifier_is_trimmed()
    {
        Assert.Equal("a wharf rat", MobNaming.Apply("a rat", "  wharf "));
    }

    [Theory]
    [InlineData("Tessa Roke, armourer")]
    [InlineData("Old Ossa")]
    [InlineData("the Creditor")]
    [InlineData("the Waiting One")]
    [InlineData("one of the owed")]
    [InlineData("someone who has been waiting")]
    [InlineData("")]
    [InlineData(null)]
    public void A_named_character_or_a_pronoun_phrase_cannot_be_modified(string? name)
    {
        Assert.False(MobNaming.CanModify(name));

        if (name is not null)
        {
            // The runtime is lenient where the API and validator refuse: the name is left alone
            // rather than mangled, because a spawner can arrive by import with anything on it.
            Assert.Equal(name, MobNaming.Apply(name, "marsh"));
        }
    }

    [Theory]
    [InlineData("a rat")]
    [InlineData("an engine")]
    [InlineData("the kept")]
    [InlineData("rat")]
    public void A_kind_name_can_be_modified(string name)
    {
        Assert.True(MobNaming.CanModify(name));
    }

    [Theory]
    [InlineData("marsh")]
    [InlineData("hill-toll")]
    [InlineData("rigger's")]
    [InlineData("long held")]
    public void A_plain_lower_case_word_is_fine(string modifier)
    {
        Assert.Null(MobNaming.Problem(modifier));
    }

    [Theory]
    [InlineData(null, "empty")]
    [InlineData("", "empty")]
    [InlineData("  ", "empty")]
    [InlineData("a marsh", "article")]
    [InlineData("The deep", "article")]
    [InlineData("Marsh", "lower-case")]
    [InlineData("marsh2", "letters")]
    [InlineData("marsh  hog", "letters")]
    [InlineData("a-very-long-modifier-that-nobody-would-type", "longer than")]
    public void A_bad_modifier_says_what_is_wrong_with_it(string? modifier, string reason)
    {
        var problem = MobNaming.Problem(modifier);

        Assert.NotNull(problem);
        Assert.Contains(reason, problem, StringComparison.Ordinal);
    }
}
