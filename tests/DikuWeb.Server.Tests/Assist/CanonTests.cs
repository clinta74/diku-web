using DikuWeb.Server.Assist;

namespace DikuWeb.Server.Tests.Assist;

/// <summary>
/// The canon prefix is present, is only the canon, and still fits the window.
/// </summary>
public sealed class CanonTests
{
    /// <summary>Measured against Gemma 3 on this exact document: 33,970 chars to 10,183 tokens.</summary>
    /// <remarks>
    /// Used instead of a tokeniser because a tokeniser is a dependency, a download and a second
    /// thing to keep in step with the model - and the question here is not "exactly how many
    /// tokens" but "has this grown past what the window can hold", which a ratio answers.
    /// </remarks>
    private const double CharsPerToken = 3.34;

    /// <summary>
    /// What the prefix may occupy of the 16,384-token window.
    /// </summary>
    /// <remarks>
    /// The rest of the budget, from <c>Modelfile.builder</c>: ~950 for the schema, ~700 for the
    /// zone's exemplars, ~600 to generate a room, and headroom. 12,000 leaves the canon room to
    /// grow by about 18% before anything has to be decided - and this test is the thing that
    /// decides it, rather than a builder noticing the model has started misremembering.
    /// </remarks>
    private const int PrefixTokenBudget = 12_000;

    [Fact]
    public void The_canon_is_embedded_and_not_empty()
    {
        Assert.False(string.IsNullOrWhiteSpace(Canon.Prefix));
        Assert.Contains("The Reaches", Canon.Prefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// It stops at the marker, so the authoring notes never reach the model.
    /// </summary>
    /// <remarks>
    /// §10 is 3,000 tokens of process - how content lands, what was retired, notes to builders. All
    /// true; none of it a fact about the world, and the budget does not have 3,000 tokens spare.
    /// </remarks>
    [Fact]
    public void It_stops_where_the_authoring_notes_begin()
    {
        Assert.DoesNotContain("Authoring notes", Canon.Prefix, StringComparison.Ordinal);
        Assert.DoesNotContain(Canon.EndMarker, Canon.Prefix, StringComparison.Ordinal);

        // But it does carry the sections that are the world.
        Assert.Contains("Yrriska", Canon.Prefix, StringComparison.Ordinal);
        Assert.Contains("Bind points", Canon.Prefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// The canon still fits, with room to work in.
    /// </summary>
    /// <remarks>
    /// <b>The test this whole feature was blocked on.</b> An over-long prompt is truncated rather
    /// than refused, so a canon that outgrows the window does not fail - it quietly stops being
    /// fully read, and the model reads as though it had learned the world and forgotten most of it.
    /// Nothing else in the system can notice that, so this is where it gets noticed.
    /// <para>
    /// If this fails, the question is which section has stopped being canon - not how to raise the
    /// number. Raising it is a decision about <c>num_ctx</c>, and that lives in the Modelfile with
    /// its own arithmetic.
    /// </para>
    /// </remarks>
    [Fact]
    public void It_fits_the_window_with_room_to_work()
    {
        var tokens = (int)(Canon.Prefix.Length / CharsPerToken);

        Assert.True(
            tokens <= PrefixTokenBudget,
            $"The canon is ~{tokens:N0} tokens, over the {PrefixTokenBudget:N0} budgeted. "
            + "Something above the canon:end marker in docs/WORLD.md has stopped being canon.");
    }

    /// <summary>
    /// Line endings are normalised, because the cache is byte-exact.
    /// </summary>
    /// <remarks>
    /// git may check this file out with either ending depending on the machine and the
    /// <c>.gitattributes</c> in force. A prefix that differs between a developer's server and the
    /// deployed one shares no KV cache with itself - measured, that is 4.4 s against 187 s - and
    /// nothing about the answers would look wrong.
    /// </remarks>
    [Fact]
    public void It_carries_no_carriage_returns()
    {
        Assert.DoesNotContain('\r', Canon.Prefix);
    }

    /// <summary>Reading it twice gives the same instance, so the prefix cannot vary per call.</summary>
    [Fact]
    public void It_is_the_same_text_every_time()
    {
        Assert.Same(Canon.Prefix, Canon.Prefix);
    }
}
