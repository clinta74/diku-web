using System.Globalization;

namespace DikuWeb.Server.Building;

/// <summary>
/// Reading a mob's loot table, which is a list of free-form bags (PLAN.md §4.8).
/// </summary>
/// <remarks>
/// <para>
/// One reader, because two would disagree. The table round-trips through jsonb, so a chance
/// arrives as a <c>double</c>, a <c>string</c>, or a <c>JsonElement</c> depending on whether it
/// came from a save, a seed, or a database read — the same trap <c>JsonBag</c> and
/// <c>StatReader</c> exist for on the engine's side of the wire, and the reason nothing here
/// pattern-matches a C# shape.
/// </para>
/// <para>
/// <b>First matching row wins.</b> A table may name one item twice — each row is an independent
/// roll on death, so that is a real thing to author — and this answers for the first, which is what
/// the reachability check has always done. Reporting a drop's chance is not the same question as
/// computing the odds of at least one, and the second is not a question anything asks yet.
/// </para>
/// </remarks>
internal static class LootTable
{
    /// <summary>The bag key naming the item a row drops.</summary>
    public const string ItemKeyField = "itemTemplateKey";

    /// <summary>The bag key holding the row's 0–1 drop chance.</summary>
    public const string ChanceField = "chance";

    /// <summary>
    /// How often this table drops <paramref name="itemKey"/>, or null when it never does.
    /// </summary>
    /// <remarks>
    /// A row that names no chance at all is certain, and reports 1. A row whose chance is zero is
    /// a table entry that can never fire, so it is not a source and reports null — which is the
    /// distinction that makes this usable as "is this a source" as well as "how often".
    /// </remarks>
    public static double? DropChance(
        IEnumerable<Dictionary<string, object>>? loot,
        string itemKey)
    {
        if (loot is null || string.IsNullOrEmpty(itemKey))
        {
            return null;
        }

        foreach (var entry in loot)
        {
            if (!entry.TryGetValue(ItemKeyField, out var keyValue)
                || !string.Equals(keyValue?.ToString(), itemKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!entry.TryGetValue(ChanceField, out var chanceValue))
            {
                return 1d;
            }

            // Unreadable is treated as certain rather than as never, so a chance mangled by a
            // round trip shows up as a drop a builder can see and fix. The alternative hides the
            // row entirely, which is how a loot table comes to look empty.
            if (!double.TryParse(
                chanceValue?.ToString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var chance))
            {
                return 1d;
            }

            return chance > 0 ? chance : null;
        }

        return null;
    }

    /// <summary>Whether this table is a source for the item at all.</summary>
    public static bool Drops(IEnumerable<Dictionary<string, object>>? loot, string itemKey) =>
        DropChance(loot, itemKey) is not null;
}
