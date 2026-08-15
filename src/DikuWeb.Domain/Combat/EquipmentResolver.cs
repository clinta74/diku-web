using DikuWeb.Domain.Items;

namespace DikuWeb.Domain.Combat;

/// <summary>
/// Extracts and resolves combat stats from character attributes and equipped items
/// (PLAN.md §4.6). Pure function that combines base stats with equipment bonuses.
///
/// Equipment provides:
/// - Weapons: weaponBonus (to attackRating), weaponDice (min/max damage)
/// - Armor: armorFlat, armorPercent (damage reduction)
/// - Other: attribute bonuses (not implemented yet, Phase 5+)
/// </summary>
public static class EquipmentResolver
{
    /// <summary>Weapon dice used when nothing in the main hand declares its own.</summary>
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
        Resolve(
            level,
            mightModifier,
            equippedMainHand,
            equippedMainHand is null ? [] : [equippedMainHand]);

    /// <summary>
    /// Resolves the main hand's attacker stats from character level, attributes, and everything
    /// equipped. Shorthand for <see cref="ResolveAttackerStatsForHand"/> with the main hand.
    /// </summary>
    /// <remarks>
    /// Weapon damage has two shapes and both are honoured. A weapon may declare explicit dice
    /// (<c>damageMin</c>/<c>damageMax</c>), and it may declare a <c>damageMultiplier</c> that
    /// scales whatever dice are in play. The multiplier is the only damage stat the builder UI
    /// offers today, so it is what most authored weapons carry; scaling the unarmed baseline is
    /// what lets such a weapon matter at all.
    ///
    /// The multiplier scales the dice only, not the flat modifier - a strong character does not
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
        ResolveAttackerStatsForHand(level, mightModifier, equipped, ItemSlot.MainHand);

    /// <summary>
    /// Resolves the stats one named hand swings with, from that hand's weapon alone.
    /// </summary>
    /// <remarks>
    /// Each hand is its own attack on its own timer, so each must be its own set of stats. The
    /// earlier shape folded both hands' multipliers into a single swing, which was right while
    /// there was only one swing to fold them into - kept now, a dual-wielder would get the
    /// off-hand multiplier on the main-hand blow *and* a whole extra attack.
    ///
    /// The hands differ in what an empty one means: an empty main hand is a fist and rolls the
    /// unarmed dice, an empty off hand does not attack and never reaches here.
    /// </remarks>
    /// <param name="hand">Which hand to resolve. Anything but a hand slot resolves as unarmed.</param>
    public static AttackerStats ResolveAttackerStatsForHand(
        int level,
        int mightModifier,
        IEnumerable<ItemInstance> equipped,
        ItemSlot hand)
    {
        ArgumentNullException.ThrowIfNull(equipped);

        var weapon = equipped
            .Where(i => i?.ResolvedStats is not null)
            .FirstOrDefault(i => i.EquippedSlot == hand);

        return Resolve(level, mightModifier, weapon, weapon is null ? [] : [weapon]);
    }

    private static AttackerStats Resolve(
        int level,
        int mightModifier,
        ItemInstance? mainHand,
        IReadOnlyList<ItemInstance> held)
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

        // Only this hand's weapon contributes its multiplier - the other hand is resolving its
        // own swing separately. The named weapon counts even if its slot is unset, which is how
        // the single-item overload behaves; ReferenceEquals keeps it from being counted twice
        // when it is also in the list.
        var damageMultiplier = 1m;

        var multiplierSources = held.Where(i =>
            i.EquippedSlot == ItemSlot.MainHand || i.EquippedSlot == ItemSlot.OffHand);

        if (mainHand is not null && mainHand.EquippedSlot != ItemSlot.MainHand)
        {
            multiplierSources = multiplierSources
                .Where(i => !ReferenceEquals(i, mainHand))
                .Append(mainHand);
        }

        foreach (var item in multiplierSources)
        {
            if (TryReadDecimal(item, "damageMultiplier", out var multiplier) && multiplier > 0m)
            {
                damageMultiplier *= multiplier;
            }
        }

        if (damageMultiplier != 1m)
        {
            // Ceiling so a multiplier can never round a die face down to nothing.
            minDamage = (int)Math.Ceiling(minDamage * damageMultiplier);
            maxDamage = (int)Math.Ceiling(maxDamage * damageMultiplier);
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

    private static bool TryReadDecimal(ItemInstance item, string key, out decimal value) =>
        StatReader.TryReadDecimal(item.ResolvedStats, key, out value);
}
