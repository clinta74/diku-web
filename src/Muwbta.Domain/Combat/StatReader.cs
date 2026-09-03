using System.Globalization;

namespace Muwbta.Domain.Combat;

/// <summary>
/// Reads numbers out of the free-form jsonb stat bags carried by items and mobs.
/// </summary>
/// <remarks>
/// These bags are deliberately schemaless (PLAN.md §4.8), so the same logical number can arrive
/// as an int, a long, a decimal, a double, a string, or a JsonElement depending on how it was
/// authored and how it round-tripped. Every call site parsing that itself is how a stat ends up
/// silently ignored - which reads as a balance problem rather than a bug, because nothing fails.
///
/// <b>Public so a test can read a bag the way the engine does.</b> It was internal, which left a
/// test asserting on a resolved stat bag no honest way to inspect one — casting the boxed value
/// works only for a bag the test hand-built in C#, and §12 is explicit that such a bag proves
/// nothing about the running game, where every value has been through jsonb and comes back a
/// <c>JsonElement</c>.
/// </remarks>
public static class StatReader
{
    public static bool TryReadInt(IReadOnlyDictionary<string, object>? stats, string key, out int value)
    {
        value = 0;

        return Raw(stats, key) is { } text &&
               int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    public static bool TryReadDecimal(IReadOnlyDictionary<string, object>? stats, string key, out decimal value)
    {
        value = 0m;

        return Raw(stats, key) is { } text &&
               decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// Reads a damage range written either as "4-7" or as a single number, the two shapes
    /// MobTemplate documents for its <c>damage</c> stat.
    /// </summary>
    public static bool TryReadRange(
        IReadOnlyDictionary<string, object>? stats,
        string key,
        out int min,
        out int max)
    {
        min = 0;
        max = 0;

        if (Raw(stats, key) is not { } text || text.Length == 0)
        {
            return false;
        }

        // Split on the last hyphen so a negative lower bound still parses.
        var separator = text.IndexOf('-', startIndex: 1);

        if (separator > 0)
        {
            var lower = text[..separator];
            var upper = text[(separator + 1)..];

            if (int.TryParse(lower, NumberStyles.Integer, CultureInfo.InvariantCulture, out min) &&
                int.TryParse(upper, NumberStyles.Integer, CultureInfo.InvariantCulture, out max))
            {
                // An author writing "7-4" means the same thing as "4-7".
                if (min > max)
                {
                    (min, max) = (max, min);
                }

                return true;
            }

            min = 0;
            max = 0;
            return false;
        }

        // A bare number is a fixed-damage weapon: "6" is 6-6.
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var single))
        {
            min = single;
            max = single;
            return true;
        }

        return false;
    }

    private static string? Raw(IReadOnlyDictionary<string, object>? stats, string key)
    {
        if (stats is null || !stats.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        return Convert.ToString(raw, CultureInfo.InvariantCulture)?.Trim();
    }
}
