using Muwbta.Domain.Narration;

namespace Muwbta.Domain.Inhabitants;

/// <summary>
/// How a placement names the mobs it spawns: the template's name with the spawner's one-word
/// modifier put after the article (PLAN.md §4.8).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a spawner gets a say in the name at all.</b> Mob templates are global, and the whole
/// point of that is that one <c>brigand</c> row stands in Brackenfell and in the Rimwalk and in
/// Grask's cut, scaled by each zone's dials. But the <em>name</em> lives on the template too, so
/// every zone that wanted "a marsh brigand" rather than "a brigand" had to author a second row
/// that differed in one word — which is how the Reaches came to carry sixty-eight templates for
/// eighteen zones and how a lurcher in Ossara and a stilt lurker in Grask stopped reading as the
/// same animal. The modifier is the one word the placement owns.
/// </para>
/// <para>
/// <b>Applied once, at spawn, into the instance's display name.</b> Nothing downstream knows a
/// modifier exists: name matching is derived from the display name, so "a marsh brigand" answers
/// to <c>marsh</c> and <c>brigand</c> for free; room ordinals key on it; and every verb that
/// prints a mob prints what it was called when it appeared.
/// </para>
/// <para>
/// <b>Article first, then the word, then the rest.</b> "a rat" + <c>wharf</c> is "a wharf rat";
/// "an ox" + <c>old</c> is "an old ox"; "an engine" + <c>hall</c> is "a hall engine" — the
/// article is re-picked for the word that now follows it, because "an hall engine" is exactly the
/// kind of line that appears forty times in one fight. A bare name is simply prefixed, and gets
/// its article later from <see cref="NarrationHelper.WithArticle"/> as it always did.
/// </para>
/// <para>
/// <b>A named character is never modified.</b> "Tessa Roke" and "the Creditor" are people, and a
/// placement that turned one into "Tessa marsh Roke" would be a defect however it was asked for.
/// The runtime leaves such a name alone; the builder API and the bundle validator refuse the
/// modifier outright, where the mistake is still cheap. The same goes for the pronoun forms
/// ("one of the owed") that head their own phrase — there is nowhere in them to put a word.
/// </para>
/// </remarks>
public static class MobNaming
{
    /// <summary>Longest modifier accepted. Two short words, at most.</summary>
    public const int MaxModifierLength = 32;

    private static readonly string[] Articles = ["a", "an", "the"];

    private static readonly string[] Pronouns = ["one", "someone", "somebody", "something"];

    /// <summary>
    /// The display name a mob spawned from <paramref name="templateName"/> gets under
    /// <paramref name="modifier"/>. The template name unchanged when the modifier is absent or
    /// the name cannot take one.
    /// </summary>
    public static string Apply(string templateName, string? modifier)
    {
        ArgumentNullException.ThrowIfNull(templateName);

        if (string.IsNullOrWhiteSpace(modifier) || !CanModify(templateName))
        {
            return templateName;
        }

        var word = modifier.Trim();
        var (article, rest) = SplitArticle(templateName);

        if (article is null)
        {
            return $"{word} {templateName}";
        }

        return article.Equals("the", StringComparison.OrdinalIgnoreCase)
            ? $"{article} {word} {rest}"
            : $"{NarrationHelper.GetArticle(word)} {word} {rest}";
    }

    /// <summary>
    /// Whether this template name has somewhere to put a modifier: not a proper name, in either
    /// its bare form or after an article, and not a pronoun phrase.
    /// </summary>
    public static bool CanModify(string? templateName)
    {
        if (string.IsNullOrWhiteSpace(templateName))
        {
            return false;
        }

        var name = templateName.Trim();

        if (NarrationHelper.IsProperName(name) || Opens(name, Pronouns))
        {
            return false;
        }

        var (article, rest) = SplitArticle(name);

        // "the Creditor", "the Waiting One": an article on a capitalised name is still a name.
        return article is null || !NarrationHelper.IsProperName(rest);
    }

    /// <summary>
    /// Why <paramref name="modifier"/> is not an acceptable modifier, as a phrase that follows the
    /// word itself in a sentence — or null when it is fine.
    /// </summary>
    /// <remarks>
    /// Judged on the trimmed text, since that is what <see cref="Apply"/> uses. An empty or blank
    /// modifier is a problem here rather than a "none": callers that mean <em>none</em> pass null,
    /// and the builder API turns an empty string into null before asking.
    /// </remarks>
    public static string? Problem(string? modifier)
    {
        if (string.IsNullOrWhiteSpace(modifier))
        {
            return "is empty";
        }

        var word = modifier.Trim();

        if (word.Length > MaxModifierLength)
        {
            return $"is longer than {MaxModifierLength} characters";
        }

        if (word.Any(c => !(char.IsLetter(c) || c is '-' or '\'' or ' ')) || word.Contains("  ", StringComparison.Ordinal))
        {
            return "may only use letters, hyphens and apostrophes";
        }

        if (Opens(word, Articles))
        {
            return "should not start with an article; the article is chosen for the whole name";
        }

        if (NarrationHelper.IsProperName(word))
        {
            return "should be lower-case; a capital would turn the mob into a named character";
        }

        return null;
    }

    /// <summary>The leading article and what follows it, or (null, name) when there is none.</summary>
    private static (string? Article, string Remainder) SplitArticle(string name)
    {
        var space = name.IndexOf(' ', StringComparison.Ordinal);

        if (space <= 0 || space == name.Length - 1)
        {
            return (null, name);
        }

        var first = name[..space];

        return Articles.Any(a => first.Equals(a, StringComparison.OrdinalIgnoreCase))
            ? (first, name[(space + 1)..].TrimStart())
            : (null, name);
    }

    private static bool Opens(string text, string[] words)
    {
        var space = text.IndexOf(' ', StringComparison.Ordinal);
        var first = space < 0 ? text : text[..space];

        return words.Any(w => first.Equals(w, StringComparison.OrdinalIgnoreCase));
    }
}
