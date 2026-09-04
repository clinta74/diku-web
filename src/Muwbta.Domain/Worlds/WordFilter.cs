using System.Text.RegularExpressions;

namespace Muwbta.Domain.Worlds;

/// <summary>
/// The words a deployment has decided nobody says here, compiled once from the active
/// configuration's list and asked about every line of speech.
/// </summary>
/// <remarks>
/// <b>Optional, and off by default.</b> An empty list is <see cref="None"/>, which matches
/// nothing and costs nothing. A builder who wants one types it into the active configuration -
/// one word per line, or separated by commas or spaces - and it reaches the loop the way the
/// welcome message does, without a restart.
///
/// <b>Whole words only.</b> A substring rule would refuse the town of Scunthorpe and the word
/// "assassin", and a filter that refuses honest speech gets turned off, at which point it protects
/// nobody. So each entry matches only where it stands alone between non-letters. That does mean
/// a determined player can spell around it; the filter is for the casual case, and the report
/// button - a moderator, a mute, a ban - is for the determined one.
///
/// <b>Not clever.</b> No leetspeak decoding, no stemming, no plurals: <c>damn</c> does not catch
/// <c>damned</c> unless the list says so. Every one of those is a source of false refusals, and
/// the person maintaining the list can add the forms they mean. Case-insensitive throughout.
/// </remarks>
public sealed class WordFilter
{
    /// <summary>Matches nothing. What a configuration with no list compiles to.</summary>
    public static readonly WordFilter None = new(null, []);

    private static readonly char[] Separators = ['\n', '\r', ',', ';', ' ', '\t'];

    private readonly Regex? _pattern;

    private WordFilter(Regex? pattern, IReadOnlyList<string> words)
    {
        _pattern = pattern;
        Words = words;
    }

    /// <summary>The list as compiled: trimmed, de-duplicated, in the order given.</summary>
    public IReadOnlyList<string> Words { get; }

    public bool IsEmpty => _pattern is null;

    /// <summary>
    /// Compiles a list. Separators are newlines, commas, semicolons and spaces; anything else
    /// in an entry is matched literally.
    /// </summary>
    public static WordFilter Parse(string? list)
    {
        var words = (list ?? string.Empty)
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (words.Count == 0)
        {
            return None;
        }

        // Escaped, so an entry is a word and never a pattern - a list is edited by builders, not
        // by people who should have to know what a backslash means to a regex. Bounded by
        // letter-or-digit rather than \b so that an entry ending in punctuation still terminates
        // at the next word; the timeout is belt and braces against a list that somehow grows
        // pathological, since escaped alternation cannot backtrack badly on its own.
        var alternation = string.Join("|", words.Select(Regex.Escape));
        var pattern = new Regex(
            $@"(?<![\p{{L}}\p{{N}}])(?:{alternation})(?![\p{{L}}\p{{N}}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));

        return new WordFilter(pattern, words);
    }

    /// <summary>Whether the text contains a listed word, and which one.</summary>
    public bool Matches(string? text, out string word)
    {
        word = string.Empty;

        if (_pattern is null || string.IsNullOrEmpty(text))
        {
            return false;
        }

        try
        {
            var match = _pattern.Match(text);
            if (!match.Success)
            {
                return false;
            }

            word = match.Value;
            return true;
        }
        catch (RegexMatchTimeoutException)
        {
            // A line that takes the filter a tenth of a second is refused rather than let
            // through: the failure mode of a language filter should never be "and then it said it".
            word = "(unreadable)";
            return true;
        }
    }
}
