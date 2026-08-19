using DikuWeb.Engine.Quests;

namespace DikuWeb.Engine.Tests;

/// <summary>
/// Reading the words a giver marked in their own offer (PLAN.md §4.9).
/// </summary>
/// <remarks>
/// The parser is tiny and the rules it enforces are not, because everything downstream trusts it:
/// the keyword a click sends comes from here, the link a player sees comes from here, and the
/// import refuses content this cannot read.
/// </remarks>
public sealed class QuestOfferTests
{
    [Fact]
    public void An_unmarked_line_is_one_piece_of_prose()
    {
        var segments = QuestOffer.Parse("Bring me the ledger.");

        var only = Assert.Single(segments);
        Assert.Equal("Bring me the ledger.", only.Text);
        Assert.False(only.IsLink);
    }

    [Fact]
    public void A_marker_splits_the_line_around_it()
    {
        var segments = QuestOffer.Parse("Somebody is missing those <things>.");

        Assert.Equal(
            [("Somebody is missing those ", false), ("things", true), (".", false)],
            segments.Select(s => (s.Text, s.IsLink)));
    }

    [Fact]
    public void A_marker_at_either_end_leaves_no_empty_prose()
    {
        Assert.Equal(
            [("Stones", true), (" came down.", false)],
            QuestOffer.Parse("<Stones> came down.").Select(s => (s.Text, s.IsLink)));

        Assert.Equal(
            [("Bring me ", false), ("five pages", true)],
            QuestOffer.Parse("Bring me <five pages>").Select(s => (s.Text, s.IsLink)));
    }

    [Fact]
    public void A_marker_can_be_several_words()
    {
        Assert.Equal(["five worn ones"], QuestOffer.Keywords("Bring me <five worn ones>."));
    }

    /// <summary>
    /// More than one marker is read, which is what makes an offer with two ways in possible —
    /// and what the collision rule in the bundle validator exists to keep honest.
    /// </summary>
    [Fact]
    public void Every_marker_in_the_line_is_a_keyword()
    {
        Assert.Equal(
            ["stones", "gate"],
            QuestOffer.Keywords("The <stones> came down and the <gate> stopped."));
    }

    [Fact]
    public void The_plain_line_is_what_a_client_without_links_would_show()
    {
        Assert.Equal(
            "Somebody is missing those things.",
            QuestOffer.Plain("Somebody is missing those <things>."));
    }

    /// <summary>
    /// Keywords keep their capitals, because the marker is displayed exactly as it was written.
    /// </summary>
    /// <remarks>
    /// The command built from one is lowercased where it is built: matching ignores case, and
    /// what the player watches appear in their transcript should be what they would have typed.
    /// </remarks>
    [Fact]
    public void A_keyword_is_kept_as_it_was_written()
    {
        Assert.Equal(["The stones"], QuestOffer.Keywords("<The stones> came down."));
    }

    // -----------------------------------------------------------------------
    // Malformed
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("Bring me those <things.", "never closed")]
    [InlineData("Bring me those things>.", "never opened")]
    [InlineData("Bring me those <>.", "marks no words")]
    [InlineData("Bring me those < >.", "marks no words")]
    [InlineData("Bring me <those <things>>.", "second marker")]
    public void A_broken_marker_is_named(string offer, string complaint)
    {
        Assert.Contains(complaint, QuestOffer.Malformed(offer), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Bring me the ledger.")]
    [InlineData("Bring me those <things>.")]
    [InlineData("The <stones> came down and the <gate> stopped.")]
    [InlineData("")]
    [InlineData(null)]
    public void A_line_that_reads_is_not_complained_about(string? offer)
    {
        Assert.Null(QuestOffer.Malformed(offer));
    }

    /// <summary>
    /// A broken line falls open: the prose survives with its punctuation showing, rather than the
    /// rest of the sentence being swallowed by a marker nobody closed.
    /// </summary>
    /// <remarks>
    /// Which is the behaviour that lets the builder see their own mistake in the room. The import
    /// refuses it long before a player gets there.
    /// </remarks>
    [Fact]
    public void A_broken_line_is_left_exactly_as_it_was_written()
    {
        const string Offer = "Somebody is missing those <things.";

        Assert.Equal(Offer, QuestOffer.Plain(Offer));
        Assert.Empty(QuestOffer.Keywords(Offer));

        var only = Assert.Single(QuestOffer.Parse(Offer));
        Assert.False(only.IsLink);
    }

    [Fact]
    public void An_empty_line_has_nothing_in_it()
    {
        Assert.Empty(QuestOffer.Parse(null));
        Assert.Empty(QuestOffer.Keywords(null));
        Assert.Equal(string.Empty, QuestOffer.Plain(null));
    }

    /// <summary>
    /// The excerpt points at the break, so a builder reading an import failure can find it in a
    /// line of prose several sentences long.
    /// </summary>
    [Fact]
    public void The_complaint_quotes_the_words_around_the_break()
    {
        var complaint = QuestOffer.Malformed(
            "Pell has a map with more crossings-out than lines. The <stones came down.");

        Assert.Contains("stones came down", complaint, StringComparison.Ordinal);
        Assert.DoesNotContain("Pell has a map", complaint, StringComparison.Ordinal);
    }
}
