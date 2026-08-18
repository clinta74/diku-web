using DikuWeb.Domain.Items;

namespace DikuWeb.Domain.Combat;

/// <summary>
/// Extracts and resolves combat stats from character attributes and equipped items
/// (PLAN.md §4.6). Pure function that combines base stats with equipment bonuses.
///
/// Equipment provides:
/// - Weapons: <c>bonus</c> (to attack rating), <c>damageMin</c>/<c>damageMax</c>, <c>baseDamage</c>
/// - Armour: <c>armor</c> (a rating, through <see cref="ArmorCurve"/>) and <c>defense</c>
/// - Other: attribute bonuses (not implemented yet, Phase 5+)
///
/// <b>This list is the contract the builder transcribes</b>, and it named the retired
/// <c>armorFlat</c>/<c>armorPercent</c> pair for long enough that both editors were still offering
/// them. <see cref="KnownStatKeys"/> is now the machine-readable version, so the drift is caught
/// rather than described.
/// </summary>
public static class EquipmentResolver
{
    /// <summary>
    /// Every <c>baseStats</c> key this class reads. A key outside this set reaches nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Named here for the reason <c>MobBehavior.KnownKeys</c> and <c>QuestDialogue.All</c> are: a
    /// literal typed at the lookup site cannot be checked by anything, and this bag has already
    /// carried dead keys twice — the retired <c>armorFlat</c>/<c>armorPercent</c>/
    /// <c>armorMultiplier</c> trio, and the three vital multipliers no version ever read.
    /// </para>
    /// <para>
    /// <b><c>damageMultiplier</c> is deliberately absent.</b> It scaled whatever dice the hand had,
    /// which for every weapon in the game meant scaling the unarmed 1–2 — so a weapon's damage was
    /// <c>ceil(1×m)</c>–<c>ceil(2×m)</c> and nothing about the authored number said so. 22 distinct
    /// multipliers across 35 weapons resolved to 14 distinct dice, and the builder's own form told
    /// authors a multiplier without dice does nothing, which was false and was how all 35 were
    /// written. Weapons declare <c>damageMin</c>/<c>damageMax</c> now. A mob attack keeps its own
    /// <c>DamageMultiplier</c>, which scales a real resolved quantity rather than a hidden constant.
    /// </para>
    /// </remarks>
    public static IReadOnlySet<string> KnownStatKeys { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "bonus",
        "damageMin",
        "damageMax",
        "baseDamage",
        "armor",
        "defense",
    };

    /// <summary>
    /// Weapon dice used when nothing in the main hand declares its own — a bare fist.
    /// </summary>
    /// <remarks>
    /// This was load-bearing for every weapon in the game until weapons declared dice: a
    /// multiplier-only weapon multiplied <em>these</em>. It now means what it says and applies to an
    /// empty hand alone.
    /// </remarks>
    private const int UnarmedMinDamage = 1;

    private const int UnarmedMaxDamage = 2;

    /// <summary>
    /// Resolves attacker stats for a single main-hand weapon. Convenience overload for callers
    /// that have no off-hand to consider.
    /// </summary>
    public static AttackerStats ResolveAttackerStats(
        int level,
        int mightModifier,
        ItemInstance? equippedMainHand) =>
        // The item is main hand by virtue of being passed here, whatever its EquippedSlot says -
        // callers using this overload are naming the hand, not asking us to search for it.
        Resolve(level, mightModifier, equippedMainHand);

    /// <summary>
    /// Resolves the main hand's attacker stats from character level, attributes, and everything
    /// equipped. Shorthand for <see cref="ResolveAttackerStatsForHand"/> with the main hand.
    /// </summary>
    /// <remarks>
    /// A weapon declares its dice outright (<c>damageMin</c>/<c>damageMax</c>) and an empty hand
    /// rolls the unarmed baseline. There used to be a third shape - a <c>damageMultiplier</c>
    /// scaling whatever dice were in play - and because it was the only damage stat the builder
    /// offered, every authored weapon carried one and none carried dice. So every weapon in the
    /// game was the unarmed 1-2 scaled, and its authored number said nothing about what it hit for.
    ///
    /// <c>baseDamage</c> is added after the roll and was never scaled: a strong character does not
    /// get their Might doubled by picking up a better sword.
    /// </remarks>
    /// <param name="level">Character level (used to derive base attackRating).</param>
    /// <param name="mightModifier">Might attribute modifier (d20 formula: (value-10)/2 rounded down).</param>
    /// <param name="equipped">Every equipped item; hands are selected from it.</param>
    /// <returns>AttackerStats with all bonuses applied.</returns>
    public static AttackerStats ResolveAttackerStats(
        int level,
        int mightModifier,
        IEnumerable<ItemInstance> equipped) =>
        // The share is an off-hand concept; the main hand always swings whole.
        ResolveAttackerStatsForHand(level, mightModifier, equipped, ItemSlot.MainHand, 1m);

    /// <summary>
    /// Resolves the stats one named hand swings with, from that hand's weapon alone.
    /// </summary>
    /// <remarks>
    /// Each hand is its own attack on its own timer, so each must be its own set of stats - this
    /// reads one hand's weapon and nothing else, so a main-hand swing cannot see what is in the off
    /// hand. That mattered acutely while weapons carried multipliers, since an earlier shape folded
    /// both hands' into a single swing and a dual-wielder got the off-hand multiplier on the
    /// main-hand blow *and* a whole extra attack. It still holds for dice.
    ///
    /// The hands differ in what an empty one means: an empty main hand is a fist and rolls the
    /// unarmed dice, an empty off hand does not attack and never reaches here.
    /// </remarks>
    /// <param name="hand">Which hand to resolve. Anything but a hand slot resolves as unarmed.</param>
    /// <param name="offHandShare">
    /// The fraction of its damage an off-hand weapon deals, from
    /// <c>AbilityProgression.OffHandDamageShare</c>. Ignored for any other hand.
    ///
    /// <b>Required rather than defaulted, and applied here rather than by the caller.</b> Combat and
    /// the <c>stats</c> screen both resolve an off hand, and a share applied in one and forgotten in
    /// the other is a screen that reports a damage range the weapon will never roll - which is the
    /// exact lie that screen was rewritten to stop telling. With no default, a new caller cannot
    /// omit it by accident.
    /// </param>
    public static AttackerStats ResolveAttackerStatsForHand(
        int level,
        int mightModifier,
        IEnumerable<ItemInstance> equipped,
        ItemSlot hand,
        decimal offHandShare)
    {
        ArgumentNullException.ThrowIfNull(equipped);

        var weapon = equipped
            .Where(i => i?.ResolvedStats is not null)
            .FirstOrDefault(i => i.EquippedSlot == hand);

        var stats = Resolve(level, mightModifier, weapon);

        return hand == ItemSlot.OffHand ? Scaled(stats, offHandShare) : stats;
    }

    /// <summary>
    /// An off hand's swing at the share of it the character has grown into.
    /// </summary>
    /// <remarks>
    /// <b>The whole swing, dice and flat together</b> - not the dice alone. <c>BaseDamage</c> is the
    /// Might modifier and it is added per swing, so at the levels this ramp is steepest it is the
    /// larger half: a Warden's +4 Might dwarfs two fifths of a starter weapon's 2-3 dice, and
    /// scaling only the dice would leave the ramp barely biting where it is meant to bite hardest.
    ///
    /// <b>Attack rating is untouched.</b> An off hand that <em>misses</em> more is a different and
    /// worse feeling than one that hits softer, and accuracy is not what the ramp is limiting.
    ///
    /// Floors at one damage rather than zero: a swing that lands does something, which is the same
    /// promise <c>DamageCalculator</c> makes at the other end.
    /// </remarks>
    private static AttackerStats Scaled(AttackerStats stats, decimal share)
    {
        if (share >= 1m)
        {
            return stats;
        }

        if (share <= 0m)
        {
            // Not reachable through combat - an untrained off hand never swings - but a caller
            // asking what a share of nothing looks like gets nothing rather than a full swing.
            return stats with { MinDamage = 0, MaxDamage = 0, BaseDamage = 0 };
        }

        var min = (int)Math.Round(stats.MinDamage * share, MidpointRounding.AwayFromZero);
        var max = (int)Math.Round(stats.MaxDamage * share, MidpointRounding.AwayFromZero);

        return stats with
        {
            MinDamage = Math.Max(1, min),
            MaxDamage = Math.Max(Math.Max(1, min), max),
            BaseDamage = (int)Math.Round(stats.BaseDamage * share, MidpointRounding.AwayFromZero),
        };
    }

    private static AttackerStats Resolve(
        int level,
        int mightModifier,
        ItemInstance? mainHand)
    {
        // Base attack rating: half level + Might modifier (PLAN.md §4.6)
        var baseAttackRating = (level / 2) + mightModifier;

        if (mainHand?.ResolvedStats is null)
        {
            mainHand = null;
        }

        // Weapon bonus and damage
        var weaponBonus = 0;
        var baseDamage = mightModifier;
        var minDamage = UnarmedMinDamage;
        var maxDamage = UnarmedMaxDamage;

        if (mainHand is not null)
        {
            if (TryReadInt(mainHand, "bonus", out var bonus))
            {
                weaponBonus = bonus;
            }

            if (TryReadInt(mainHand, "damageMin", out var min))
            {
                minDamage = min;
            }

            if (TryReadInt(mainHand, "damageMax", out var max))
            {
                maxDamage = max;
            }

            if (TryReadInt(mainHand, "baseDamage", out var damage))
            {
                baseDamage += damage;
            }
        }

        // A weapon authored with max below min would otherwise make the roll throw.
        maxDamage = Math.Max(minDamage, maxDamage);

        return new AttackerStats(
            AttackRating: baseAttackRating + weaponBonus,
            BaseDamage: baseDamage,
            MinDamage: minDamage,
            MaxDamage: maxDamage);
    }

    /// <summary>
    /// Resolves defender stats from character attributes and equipped armor.
    /// Filters to only armor slots (ignores weapons, trinkets, etc.).
    /// </summary>
    /// <param name="agilityModifier">Agility attribute modifier (d20 formula: (value-10)/2 rounded down).</param>
    /// <param name="equippedArmor">List of equipped items; only armor slots are used.</param>
    /// <returns>DefenderStats with all armor bonuses applied.</returns>
    /// <remarks>
    /// <b>Two authored numbers, and they do different jobs.</b> <c>armor</c> is summed and handed to
    /// <see cref="ArmorCurve"/>, deciding what a landed blow costs; <c>defense</c> is summed
    /// straight into the rating, deciding how often one lands. Keeping them apart is what lets a
    /// shield be evasive and a breastplate be absorbent — one number could only have made every
    /// piece both, in fixed proportion.
    ///
    /// The old vocabulary is gone rather than deprecated. <c>armorFlat</c> was subtracted from each
    /// blow, which is unusable at scale (see <see cref="ArmorCurve"/>); <c>armorPercent</c> was a
    /// second, redundant way to say what the curve now says; and <c>armorMultiplier</c> scaled the
    /// whole set's total from any one piece, so six pieces at 1.2 multiplied to 2.99 and a piece
    /// carrying only a multiplier silently granted nothing at all.
    /// </remarks>
    public static DefenderStats ResolveDefenderStats(
        int level,
        int agilityModifier,
        IEnumerable<ItemInstance> equippedArmor)
    {
        // Base defense: 10 + level/2 + Agility modifier (PLAN.md §4.6)
        // Note: the base and the level term are baked into the formula; we only add the rest
        var armorDefense = agilityModifier;
        var armor = 0;

        // Accumulate armor from armor-slot items only
        var armorItems = equippedArmor
            .Where(i => i.EquippedSlot.HasValue && IsArmorSlot(i.EquippedSlot.Value))
            .ToList();

        foreach (var piece in armorItems)
        {
            if (piece?.ResolvedStats is null)
            {
                continue;
            }

            if (TryReadInt(piece, "armor", out var rating))
            {
                armor += rating;
            }

            if (TryReadInt(piece, "defense", out var def))
            {
                armorDefense += def;
            }
        }

        return new DefenderStats(
            Level: level,
            DefenseRating: armorDefense,
            Armor: armor);
    }

    /// <remarks>
    /// <b>Trinket counts, and used not to.</b> It was absent here and is not one of the two hands a
    /// damage multiplier is read from, so the eighth slot equipped and did nothing whatsoever — an
    /// item could be authored, bought, and worn without any stat on it ever being read. A trinket is
    /// not armour in the fiction, but it is worn and it is protective, and the alternative was
    /// giving one slot a vocabulary of its own.
    ///
    /// The main hand stays out: a weapon's numbers are the attacker's, resolved per hand.
    /// </remarks>
    private static bool IsArmorSlot(ItemSlot slot) =>
        slot is ItemSlot.Head or ItemSlot.Chest or ItemSlot.Hands or
                ItemSlot.Legs or ItemSlot.Feet or ItemSlot.OffHand or ItemSlot.Trinket;

    private static bool TryReadInt(ItemInstance item, string key, out int value) =>
        StatReader.TryReadInt(item.ResolvedStats, key, out value);

}
