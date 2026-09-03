using System.Globalization;

namespace Muwbta.Domain.Combat;

/// <summary>
/// What an item's numbers mean, in words a player reads rather than the keys a builder authors.
/// </summary>
/// <remarks>
/// <para>
/// <b>The player's screens were showing the builder's view.</b> <c>stats</c> printed the stat bag
/// as <c>armor=10, defense=1</c> and <c>bonus=4, damageMax=13, damageMin=7</c> — alphabetical, so
/// the max came before the min — which is exactly right under <c>examine</c>'s builder block and
/// wrong in front of somebody who just wants to know what their sword does. The shop had the
/// opposite problem and showed nothing at all.
/// </para>
/// <para>
/// <b>Beside <see cref="EquipmentResolver"/> on purpose.</b> Every key named here is a key that
/// class reads, and the wording has to keep step with what the number actually does: <c>bonus</c>
/// is attack rating and not damage, <c>baseDamage</c> is added after the roll rather than to the
/// dice, and <c>defense</c> makes you harder to hit while <c>armor</c> softens what lands. A
/// formatter that drifted from those would be worse than the raw keys, because it would read as
/// authoritative.
/// </para>
/// <para>
/// <b>The vocabulary is closed</b>, which is what makes this safe:
/// <see cref="EquipmentResolver.KnownStatKeys"/> is six keys and <c>BundleValidator</c> raises an
/// error for any bag key outside it, so content cannot introduce a seventh that this would
/// silently drop. <see cref="Unknown"/> is asserted against that set in the tests.
/// </para>
/// </remarks>
public static class ItemStatLine
{
    /// <summary>
    /// Keys this deliberately says nothing about.
    /// </summary>
    /// <remarks>
    /// Empty, and tested against <see cref="EquipmentResolver.KnownStatKeys"/> so it stays that
    /// way: a key the game reads and this does not mention is a number affecting the player that
    /// no screen tells them about.
    /// </remarks>
    public static IReadOnlySet<string> Unknown { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// One line describing what the item does, or null when it carries no numbers at all.
    /// </summary>
    /// <remarks>
    /// Null rather than an empty string or a dash, so a caller decides whether a plain item is
    /// worth a line of its own. A cloak with no stats is not "Armour 0"; it is a cloak.
    /// </remarks>
    public static string? For(IReadOnlyDictionary<string, object>? stats)
    {
        if (stats is null || stats.Count == 0)
        {
            return null;
        }

        var parts = new List<string>();

        // Damage before accuracy before defence, and the dice before the flat add, because that is
        // the order the numbers apply in and the order somebody comparing two weapons reads them.
        var hasMin = StatReader.TryReadInt(stats, "damageMin", out var min);
        var hasMax = StatReader.TryReadInt(stats, "damageMax", out var max);

        if (hasMin || hasMax)
        {
            // A weapon authored with only one side still reads, rather than printing a range with
            // a hole in it. Matching EquipmentResolver, which floors max at min.
            var low = hasMin ? min : max;
            var high = Math.Max(low, hasMax ? max : min);
            parts.Add($"Damage {low}-{high}");
        }

        if (StatReader.TryReadInt(stats, "baseDamage", out var flat) && flat != 0)
        {
            // Named as damage rather than folded into the range: it is added after the roll, so a
            // range that absorbed it would claim dice the weapon does not have.
            parts.Add($"{Signed(flat)} damage");
        }

        if (StatReader.TryReadInt(stats, "bonus", out var bonus) && bonus != 0)
        {
            // "to hit" rather than "bonus". The authored key says nothing about what it affects,
            // and it affects accuracy — a player reading "bonus=4" beside a damage range will
            // reasonably assume it is more damage.
            parts.Add($"{Signed(bonus)} to hit");
        }

        if (StatReader.TryReadInt(stats, "armor", out var armor) && armor != 0)
        {
            parts.Add($"Armour {armor}");
        }

        if (StatReader.TryReadInt(stats, "defense", out var defense) && defense != 0)
        {
            parts.Add($"{Signed(defense)} defence");
        }

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    /// <summary>
    /// How <paramref name="candidate"/> differs from <paramref name="worn"/>, or null when nothing
    /// a player can act on changes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Differences, not a verdict.</b> "Better" is the question the player asked and it is
    /// genuinely not always answerable: three more damage for one less accuracy is a trade, and a
    /// game that called that an upgrade would be guessing on their behalf. Naming what moves and
    /// in which direction is the part that was actually missing — the arithmetic, not the
    /// judgement.
    /// </para>
    /// <para>
    /// Only non-zero movements appear, so a like-for-like swap reads as the short sentence it is
    /// rather than a wall of "+0".
    /// </para>
    /// </remarks>
    public static string? Delta(
        IReadOnlyDictionary<string, object>? candidate,
        IReadOnlyDictionary<string, object>? worn)
    {
        var parts = new List<string>();

        // The dice and the flat add are one number to a player: both land as damage, and reporting
        // "+3 damage, +1 damage" would be reporting the implementation. Expressed in halves,
        // because the dice term is (see Range) and a flat point is two of them.
        var damage = Range(candidate) - Range(worn)
            + (2 * (Read(candidate, "baseDamage") - Read(worn, "baseDamage")));

        Move(parts, "damage", damage / 2m);
        Move(parts, "to hit", Read(candidate, "bonus") - Read(worn, "bonus"));
        Move(parts, "armour", Read(candidate, "armor") - Read(worn, "armor"));
        Move(parts, "defence", Read(candidate, "defense") - Read(worn, "defense"));

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    /// <summary>
    /// A weapon's average roll, doubled so it stays a whole number.
    /// </summary>
    /// <remarks>
    /// Comparing two ranges needs one number and the average is the honest one — a 2-14 weapon and
    /// a 7-9 weapon hit for the same amount over a fight and differ only in how streaky it feels.
    /// Doubled rather than rounded, so half a point of difference between two weapons is not lost
    /// on the way; the caller divides once, at the end.
    /// </remarks>
    private static int Range(IReadOnlyDictionary<string, object>? stats) =>
        Read(stats, "damageMin") + Read(stats, "damageMax");

    private static int Read(IReadOnlyDictionary<string, object>? stats, string key) =>
        StatReader.TryReadInt(stats, key, out var value) ? value : 0;

    /// <summary>
    /// Adds one movement, or nothing when the number did not move.
    /// </summary>
    /// <remarks>
    /// A half point is real — it is what separates two weapons whose ranges differ on one side
    /// only — so it is shown rather than rounded away, and the format drops the fraction when
    /// there is not one.
    /// </remarks>
    private static void Move(List<string> parts, string label, decimal change)
    {
        if (change == 0)
        {
            return;
        }

        parts.Add($"{Signed(change)} {label}");
    }

    private static string Signed(int value) =>
        value.ToString("+#;-#;+0", CultureInfo.InvariantCulture);

    private static string Signed(decimal value) =>
        value.ToString("+0.##;-0.##;+0", CultureInfo.InvariantCulture);
}
