namespace Muwbta.Engine.Commands;

/// <summary>
/// Splitting a command's argument into its first word and the rest.
/// </summary>
/// <remarks>
/// <para>
/// There were three near-identical copies of this — <c>PartyCommands.SplitFirstWord</c>,
/// <c>ChannelCommands.SplitFirstWord</c> and <c>AdminCommands.SplitName</c> — with three different
/// contracts: one lowercased the first word, one did not, and one returned nulls instead of empty
/// strings. Nothing said which was intended where, so a fourth would have been a coin toss
/// (BUGS.md #25).
/// </para>
/// <para>
/// <b>Deliberately not <see cref="CommandRegistry.Split"/>.</b> That one turns a whole input line
/// into a verb and an argument and has to honour the punctuation shortcuts (<c>'</c> for say,
/// <c>;</c> for emote), so it answers a different question about a different string. Folding the
/// two together would put shortcut handling in the middle of "which subcommand is this".
/// </para>
/// </remarks>
internal static class CommandText
{
    /// <summary>
    /// The first whitespace-separated word and whatever followed it, both trimmed.
    /// </summary>
    /// <param name="lowercase">
    /// Lowercase the first word. True for a subcommand, which is matched against a fixed
    /// vocabulary; false for a name, where the caller wants what the player typed.
    /// </param>
    /// <returns>
    /// Empty strings rather than nulls when there is nothing there. A caller wanting to
    /// distinguish "absent" tests <see cref="string.Length"/>, which is what they were all doing
    /// anyway — the null-returning copy forced two of its four callers to discard the difference
    /// immediately.
    /// </returns>
    // `Remainder`, not `Rest`: ValueTuple has a field of that name, so it is disallowed as an
    // element name at any position.
    public static (string First, string Remainder) SplitFirstWord(string? argument, bool lowercase)
    {
        var trimmed = (argument ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            return (string.Empty, string.Empty);
        }

        var space = trimmed.IndexOf(' ', StringComparison.Ordinal);

        var first = space < 0 ? trimmed : trimmed[..space];
        var rest = space < 0 ? string.Empty : trimmed[(space + 1)..].TrimStart();

        return (lowercase ? first.ToLowerInvariant() : first, rest);
    }
}
