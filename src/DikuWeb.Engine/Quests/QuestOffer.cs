namespace DikuWeb.Engine.Quests;

/// <summary>
/// The authored offer line, and the words in it a player can click to take the quest on
/// (PLAN.md §4.9).
/// </summary>
/// <remarks>
/// <para>
/// A giver's offer used to end with a dim parenthetical naming the command — stage direction
/// bolted onto the end of a sentence, and the thing a player actually wanted to click was the
/// noun sitting in the middle of the prose. The marker puts it there: <c>Somebody is missing
/// those &lt;things&gt;.</c> renders "things" as a link that runs <c>talk pell things</c>.
/// </para>
/// <para>
/// <b>The marked words are the keyword.</b> Nothing else is authored, and there is no second
/// field to keep in step — which is the whole argument for parsing the prose rather than adding
/// <c>Quest.Keywords</c>. A separate list can name a word the sentence does not contain, and the
/// only way anyone would find out is a player clicking a link that does nothing.
/// </para>
/// <para>
/// <b>Angle brackets, and not the obvious alternatives.</b> Square brackets are what a writer
/// reaches for when inserting an aside, and this prose is full of people trailing off. Braces are
/// taken: <c>{name}</c> already means <em>substitute a value</em> in the login greeting, and one
/// bracket meaning two things in text the same builder edits is the collision worth dodging.
/// Angle brackets appear nowhere in the Reaches — not in prose, not even in the single-character
/// item icons, where the pipes and square brackets turned out to live.
/// </para>
/// <para>
/// <b>Malformed text falls open.</b> An unclosed marker yields the whole line as prose with the
/// brackets left visible, rather than swallowing the rest of the sentence — a builder sees their
/// own mistake in the room. <see cref="Malformed"/> is what turns it into an import error, which
/// is where it should be caught.
/// </para>
/// </remarks>
public static class QuestOffer
{
    private const char Open = '<';
    private const char Close = '>';

    /// <summary>A run of the offer line: either prose, or words that take the quest on.</summary>
    public readonly record struct OfferSegment(string Text, bool IsLink);

    /// <summary>
    /// The offer split into prose and links. A line with no markers is one prose segment.
    /// </summary>
    public static IReadOnlyList<OfferSegment> Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        if (Malformed(text) is not null)
        {
            return [new OfferSegment(text, false)];
        }

        var segments = new List<OfferSegment>();
        var at = 0;

        while (at < text.Length)
        {
            var open = text.IndexOf(Open, at);

            if (open < 0)
            {
                segments.Add(new OfferSegment(text[at..], false));
                break;
            }

            if (open > at)
            {
                segments.Add(new OfferSegment(text[at..open], false));
            }

            var close = text.IndexOf(Close, open + 1);
            segments.Add(new OfferSegment(text[(open + 1)..close], true));
            at = close + 1;
        }

        return segments;
    }

    /// <summary>The offer with its markers taken out, for anywhere a link cannot be shown.</summary>
    public static string Plain(string? text) =>
        string.IsNullOrEmpty(text)
            ? string.Empty
            : string.Concat(Parse(text).Select(segment => segment.Text));

    /// <summary>
    /// The marked phrases, which are the words this quest answers to.
    /// </summary>
    /// <remarks>
    /// Kept verbatim rather than lowercased, because a marker is displayed as written. The
    /// command built from it is lowercased at the call site: matching ignores case, and what
    /// the player sees echoed back should be what they would have typed.
    /// </remarks>
    public static IReadOnlyList<string> Keywords(string? text) =>
        [.. Parse(text).Where(s => s.IsLink).Select(s => s.Text.Trim()).Where(s => s.Length > 0)];

    /// <summary>
    /// Why this line's markers cannot be read, or null when they are fine.
    /// </summary>
    /// <remarks>
    /// Phrased as a whole sentence because it is shown to a builder by the bundle validator and
    /// by the quest editor, neither of which can add useful context to it.
    /// </remarks>
    public static string? Malformed(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var depth = 0;
        var openedAt = -1;

        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case Open when depth > 0:
                    return $"'{Excerpt(text, openedAt)}' opens a second marker inside one that is "
                        + "still open";

                case Open:
                    depth = 1;
                    openedAt = i;
                    break;

                case Close when depth == 0:
                    return $"'{Excerpt(text, i)}' closes a marker that was never opened";

                case Close when text[(openedAt + 1)..i].Trim().Length == 0:
                    return $"'{Excerpt(text, openedAt)}' marks no words";

                case Close:
                    depth = 0;
                    break;
            }
        }

        return depth == 0 ? null : $"'{Excerpt(text, openedAt)}' is never closed";
    }

    /// <summary>Enough of the line around a bad marker for a builder to find it.</summary>
    private static string Excerpt(string text, int at)
    {
        var from = Math.Max(0, at - 12);
        var to = Math.Min(text.Length, at + 20);

        return (from > 0 ? "…" : string.Empty)
            + text[from..to]
            + (to < text.Length ? "…" : string.Empty);
    }
}
