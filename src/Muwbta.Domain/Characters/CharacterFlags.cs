namespace Muwbta.Domain.Characters;

/// <summary>
/// What a character flag key may look like (PLAN.md §4.15).
/// </summary>
/// <remarks>
/// <b>Deliberately not a registry, unlike <see cref="Worlds.RoomFlags"/>.</b> That one is closed
/// because every flag in it has engine behaviour attached — <c>pvp</c> reaches combat, <c>dark</c>
/// reaches the room description — so registering a flag and writing its reader are one act. A
/// character flag has no engine behaviour whatsoever. Its only meaning is that some exit asks for
/// it, which makes the set of real flags a property of the authored world rather than of this
/// assembly. A <c>Register</c> call per realm would put content in Domain and mean shipping a
/// binary to open a new Reach.
///
/// What replaces the registry is reachability. <c>/validate</c> reports an exit asking for a flag
/// no quest grants, which is the same check — and the same class of bug — as a quest item nothing
/// drops (§7.4). A typo is caught by nothing being able to grant it, not by a lookup table.
///
/// Absence is still the safe value: a character who does not hold the flag does not pass.
/// </remarks>
public static class CharacterFlags
{
    /// <summary>
    /// Long enough for <c>attuned.the-unlit</c> and short enough to stay a key rather than a
    /// sentence.
    /// </summary>
    public const int MaxLength = 64;

    /// <summary>
    /// Lowercase letters, digits, hyphens, and dots as namespace separators — the same narrow
    /// alphabet <see cref="Worlds.RoomKey"/> uses, plus the dot, so <c>attuned.grask</c> reads as
    /// one family. Narrow for the same reason: these appear in URLs and in builder output, and
    /// case-insensitive comparison bugs are avoided by not permitting case at all.
    /// </summary>
    public static bool IsValidKey(string? key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > MaxLength)
        {
            return false;
        }

        // A leading or trailing separator reads as a typo, and an empty segment ("a..b") would
        // make two different-looking keys mean the same thing.
        if (key[0] is '-' or '.' || key[^1] is '-' or '.')
        {
            return false;
        }

        var previousWasDot = false;

        foreach (var c in key)
        {
            var ok = c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '.';
            if (!ok)
            {
                return false;
            }

            if (c == '.' && previousWasDot)
            {
                return false;
            }

            previousWasDot = c == '.';
        }

        return true;
    }
}
