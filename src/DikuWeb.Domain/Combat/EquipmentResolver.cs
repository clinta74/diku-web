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
    /// <summary>
    /// Resolves attacker stats from character level, attributes, and equipped weapon.
    /// </summary>
    /// <param name="level">Character level (used to derive base attackRating).</param>
    /// <param name="mightModifier">Might attribute modifier (d20 formula: (value-10)/2 rounded down).</param>
    /// <param name="equippedMainHand">Equipped main-hand weapon, or null for unarmed.</param>
    /// <returns>AttackerStats with all bonuses applied.</returns>
    public static AttackerStats ResolveAttackerStats(
        int level,
        int mightModifier,
        ItemInstance? equippedMainHand)
    {
        // Base attack rating: half level + Might modifier (PLAN.md §4.6)
        int baseAttackRating = (level / 2) + mightModifier;

        // Weapon bonus and damage
        int weaponBonus = 0;
        int baseDamage = mightModifier;
        int minDamage = 1;
        int maxDamage = 2;

        if (equippedMainHand?.ResolvedStats is not null)
        {
            // Extract weapon bonus and dice from equipped weapon
            if (equippedMainHand.ResolvedStats.TryGetValue("bonus", out var bonusObj) &&
                int.TryParse(bonusObj?.ToString(), out var bonus))
            {
                weaponBonus = bonus;
            }

            if (equippedMainHand.ResolvedStats.TryGetValue("damageMin", out var minObj) &&
                int.TryParse(minObj?.ToString(), out var min))
            {
                minDamage = min;
            }

            if (equippedMainHand.ResolvedStats.TryGetValue("damageMax", out var maxObj) &&
                int.TryParse(maxObj?.ToString(), out var max))
            {
                maxDamage = max;
            }

            if (equippedMainHand.ResolvedStats.TryGetValue("baseDamage", out var damageObj) &&
                int.TryParse(damageObj?.ToString(), out var damage))
            {
                baseDamage += damage;
            }
        }

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
    public static DefenderStats ResolveDefenderStats(
        int agilityModifier,
        IEnumerable<ItemInstance> equippedArmor)
    {
        // Base defense: 10 + Agility modifier (PLAN.md §4.6)
        // Note: base of 10 is baked into the formula; we only add modifier and armor
        int armorDefense = agilityModifier;
        int armorFlat = 0;
        decimal armorPercent = 0m;

        // Accumulate armor from armor-slot items only
        var armorItems = equippedArmor
            .Where(i => i.EquippedSlot.HasValue && IsArmorSlot(i.EquippedSlot.Value))
            .ToList();

        foreach (var armor in armorItems)
        {
            if (armor?.ResolvedStats is null)
            {
                continue;
            }

            if (armor.ResolvedStats.TryGetValue("armorFlat", out var flatObj) &&
                int.TryParse(flatObj?.ToString(), out var flat))
            {
                armorFlat += flat;
            }

            if (armor.ResolvedStats.TryGetValue("armorPercent", out var percentObj) &&
                decimal.TryParse(percentObj?.ToString(), out var percent))
            {
                armorPercent += percent;
            }

            if (armor.ResolvedStats.TryGetValue("defense", out var defObj) &&
                int.TryParse(defObj?.ToString(), out var def))
            {
                armorDefense += def;
            }
        }

        // Clamp percent armor to reasonable bounds (0-95%)
        armorPercent = Math.Clamp(armorPercent, 0m, 0.95m);

        return new DefenderStats(
            DefenseRating: armorDefense,
            ArmorFlat: armorFlat,
            ArmorPercent: armorPercent);
    }

    private static bool IsArmorSlot(ItemSlot slot) =>
        slot is ItemSlot.Head or ItemSlot.Chest or ItemSlot.Hands or
                ItemSlot.Legs or ItemSlot.Feet or ItemSlot.OffHand;
}
