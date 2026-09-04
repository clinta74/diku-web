using Muwbta.Domain.Worlds;

namespace Muwbta.Domain.Tests.Worlds;

public sealed class WordFilterTests
{
    [Fact]
    public void An_empty_list_matches_nothing_and_is_the_shared_instance()
    {
        Assert.Same(WordFilter.None, WordFilter.Parse(null));
        Assert.Same(WordFilter.None, WordFilter.Parse("  \n , ; "));
        Assert.False(WordFilter.None.Matches("anything at all", out _));
        Assert.True(WordFilter.None.IsEmpty);
    }

    [Theory]
    [InlineData("blort\nzarg", "you utter blort", "blort")]
    [InlineData("blort, zarg", "ZARG!", "ZARG")]
    [InlineData("blort zarg; flib", "a flib in the road", "flib")]
    [InlineData("blort", "Blort.", "Blort")]
    [InlineData("blort", "'blort'", "blort")]
    public void A_listed_word_is_found_whole_in_any_case(string list, string text, string expected)
    {
        var filter = WordFilter.Parse(list);

        Assert.True(filter.Matches(text, out var word));
        Assert.Equal(expected, word);
    }

    [Theory]
    [InlineData("ass", "an assassin walked into Scunthorpe")]
    [InlineData("damn", "the damned river")]
    [InlineData("blort", "blorted")]
    [InlineData("blort", "unblort")]
    public void A_word_inside_another_word_is_not_a_match(string list, string text) =>
        Assert.False(WordFilter.Parse(list).Matches(text, out _));

    [Fact]
    public void Entries_are_words_not_patterns()
    {
        // A builder typing a dot or a bracket means that character, not "any character".
        var filter = WordFilter.Parse("b.rt (x)");

        Assert.True(filter.Matches("b.rt", out _));
        Assert.False(filter.Matches("bart", out _));
        Assert.True(filter.Matches("say (x) please", out _));
    }

    [Fact]
    public void Duplicates_collapse_and_order_is_kept()
    {
        var filter = WordFilter.Parse("zarg\nBlort\nzarg\nBLORT");

        Assert.Equal(["zarg", "Blort"], filter.Words);
    }

    [Fact]
    public void A_name_is_one_word_and_is_judged_as_one()
    {
        // Character names go through the same filter, so a list entry refuses a name that is
        // exactly it and nothing that merely contains it.
        var filter = WordFilter.Parse("blort");

        Assert.True(filter.Matches("Blort", out _));
        Assert.False(filter.Matches("Blortimer", out _));
    }
}
