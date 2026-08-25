using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Items;

namespace DikuWeb.Domain.Tests.Combat;

public sealed class EquipmentResolverTests
{
    [Fact]
    public void ResolveAttackerStats_unarmed_uses_base_stats_only()
    {
        // Character with no weapon equipped
        var stats = EquipmentResolver.ResolveAttackerStats(
            level: 5,
            mightModifier: 2,
            equippedMainHand: null);

        // Accuracy: skill 1.75 x level 5 x (1 + Might 2/30) = 9.33, to 9. The whole level, and
        // the attribute as a share of it rather than a flat addition (DamageCalculator.GearScale).
        Assert.Equal(9, stats.AttackRating);
        // Base damage: Might modifier only
        Assert.Equal(2, stats.BaseDamage);
        // Unarmed: 1d2
        Assert.Equal(1, stats.MinDamage);
        Assert.Equal(2, stats.MaxDamage);
    }

    [Fact]
    public void ResolveAttackerStats_weapon_adds_bonus()
    {
        var weapon = new ItemInstance
        {
            TemplateKey = "steel-sword",
            ResolvedStats = new Dictionary<string, object>
            {
                { "bonus", 3 },
                { "damageMin", 6 },
                { "damageMax", 10 },
                { "baseDamage", 2 },
            }
        };

        var stats = EquipmentResolver.ResolveAttackerStats(
            level: 5,
            mightModifier: 1,
            equippedMainHand: weapon);

        // Accuracy: 1.75 x 5 x (1 + (Might 1 + bonus 3)/30) = 9.92, to 10.
        Assert.Equal(10, stats.AttackRating);
        // Base damage: Might 1 + weapon damage 2 = 3
        Assert.Equal(3, stats.BaseDamage);
        // Weapon dice: 6d10
        Assert.Equal(6, stats.MinDamage);
        Assert.Equal(10, stats.MaxDamage);
    }

    [Fact]
    public void ResolveAttackerStats_weapon_bonus_affects_attackrating()
    {
        var weapon = new ItemInstance
        {
            TemplateKey = "longsword",
            ResolvedStats = new Dictionary<string, object> { { "bonus", 2 } }
        };

        var unarmed = EquipmentResolver.ResolveAttackerStats(level: 10, mightModifier: 0, equippedMainHand: null);
        var armed = EquipmentResolver.ResolveAttackerStats(level: 10, mightModifier: 0, equippedMainHand: weapon);

        // The bonus is worth a share of the wielder's level rather than a flat two, so this is a
        // comparison rather than an equality: at level 10 it buys 1.75 x 10 x 2/30, a little over
        // one point. The same weapon is worth proportionally the same at level 50.
        Assert.True(armed.AttackRating > unarmed.AttackRating);
    }

    [Fact]
    public void ResolveAttackerStats_might_modifier_affects_damage()
    {
        var stats1 = EquipmentResolver.ResolveAttackerStats(level: 5, mightModifier: 0, equippedMainHand: null);
        var stats2 = EquipmentResolver.ResolveAttackerStats(level: 5, mightModifier: 3, equippedMainHand: null);

        Assert.Equal(3, stats2.BaseDamage - stats1.BaseDamage);
    }

    [Fact]
    public void ResolveAttackerStats_level_affects_attackrating()
    {
        var low = EquipmentResolver.ResolveAttackerStats(level: 4, mightModifier: 0, equippedMainHand: null);
        var high = EquipmentResolver.ResolveAttackerStats(level: 6, mightModifier: 0, equippedMainHand: null);

        // Accuracy carries the whole level now rather than half of it, times the skill factor:
        // two levels are worth 1.75 x 2 = 3.5, which lands as 4 between 7 and 11.
        Assert.Equal(4, high.AttackRating - low.AttackRating);
    }

    [Fact]
    public void ResolveDefenderStats_unarmored_uses_agility_only()
    {
        var stats = EquipmentResolver.ResolveDefenderStats(
            level: 6,
            agilityModifier: 2,
            equippedArmor: []);

        // The base 10 and the level term are baked into the formula; this adds the rest.
        Assert.Equal(6, stats.Level);
        Assert.Equal(2, stats.DefenseRating);
        Assert.Equal(0, stats.Armor);
    }

    [Fact]
    public void ResolveDefenderStats_armor_ratings_sum()
    {
        var chest = Piece("iron-chest", ItemSlot.Chest, ("armor", 30));
        var legs = Piece("iron-legs", ItemSlot.Legs, ("armor", 20));

        var stats = EquipmentResolver.ResolveDefenderStats(
            level: 1,
            agilityModifier: 1,
            equippedArmor: new[] { chest, legs });

        Assert.Equal(1, stats.DefenseRating);
        Assert.Equal(50, stats.Armor);
    }

    [Fact]
    public void ResolveDefenderStats_armor_and_defense_are_independent()
    {
        // The reason there are two authored numbers: a shield can be evasive without being
        // absorbent, and plate the other way round. One number could only have made every piece
        // both, in a fixed ratio nobody chose.
        var shield = Piece("plank-shield", ItemSlot.OffHand, ("armor", 10), ("defense", 3));
        var breastplate = Piece("iron-chest", ItemSlot.Chest, ("armor", 60));

        var stats = EquipmentResolver.ResolveDefenderStats(
            level: 1,
            agilityModifier: 2,
            equippedArmor: new[] { shield, breastplate });

        Assert.Equal(5, stats.DefenseRating); // 2 agility + 3 from the shield
        Assert.Equal(70, stats.Armor);
    }

    [Fact]
    public void ResolveDefenderStats_armor_defense_bonus()
    {
        var armor = Piece("blessed-mail", ItemSlot.Chest, ("defense", 2));

        var stats = EquipmentResolver.ResolveDefenderStats(
            level: 1,
            agilityModifier: 1,
            equippedArmor: new[] { armor });

        Assert.Equal(3, stats.DefenseRating);
    }

    [Fact]
    public void ResolveDefenderStats_ignores_the_main_hand()
    {
        // A weapon's numbers belong to the attacker and are resolved per hand.
        var weapon = Piece("sword", ItemSlot.MainHand, ("armor", 100));

        var stats = EquipmentResolver.ResolveDefenderStats(
            level: 1,
            agilityModifier: 0,
            equippedArmor: new[] { weapon });

        Assert.Equal(0, stats.Armor);
    }

    [Fact]
    public void ResolveDefenderStats_counts_a_trinket()
    {
        // It used not to, and was not a hand either, so the eighth slot equipped and did nothing
        // whatsoever - an item could be authored, sold, and worn with no stat on it ever read.
        var trinket = Piece("ring", ItemSlot.Trinket, ("armor", 12), ("defense", 1));

        var stats = EquipmentResolver.ResolveDefenderStats(
            level: 1,
            agilityModifier: 0,
            equippedArmor: new[] { trinket });

        Assert.Equal(12, stats.Armor);
        Assert.Equal(1, stats.DefenseRating);
    }

    [Fact]
    public void ResolveDefenderStats_handles_empty_resolved_stats()
    {
        var armor = new ItemInstance
        {
            TemplateKey = "plain-cloth",
            EquippedSlot = ItemSlot.Chest,
            ResolvedStats = new() // Empty stats
        };

        var stats = EquipmentResolver.ResolveDefenderStats(
            level: 1,
            agilityModifier: 1,
            equippedArmor: new[] { armor });

        Assert.Equal(1, stats.DefenseRating);
        Assert.Equal(0, stats.Armor);
    }

    private static ItemInstance Piece(
        string key, ItemSlot slot, params (string Key, object Value)[] stats) =>
        new()
        {
            TemplateKey = key,
            EquippedSlot = slot,
            ResolvedStats = stats.ToDictionary(s => s.Key, s => s.Value),
        };

    [Fact]
    public void ResolveAttackerStats_negative_modifiers()
    {
        var stats = EquipmentResolver.ResolveAttackerStats(
            level: 8,
            mightModifier: -2,
            equippedMainHand: null);

        // Accuracy: 1.75 x 8 x (1 - 2/30) = 13.07, to 13. A penalty takes a share off rather
        // than a flat amount, the same way a bonus adds one.
        Assert.Equal(13, stats.AttackRating);
        // Base damage: -2
        Assert.Equal(-2, stats.BaseDamage);
    }

    [Fact]
    public void ResolveDefenderStats_negative_agility()
    {
        var stats = EquipmentResolver.ResolveDefenderStats(
            level: 1,
            agilityModifier: -3,
            equippedArmor: []);

        Assert.Equal(-3, stats.DefenseRating);
    }

    // =========================================================================
    // Weapon dice, and the multiplier that used to stand in for them
    // =========================================================================

    /// <summary>
    /// A weapon that declares no dice swings as a bare fist.
    /// </summary>
    /// <remarks>
    /// This used to be the <em>normal</em> case rather than a mistake. <c>damageMultiplier</c> was
    /// the only damage stat the builder offered, so every authored weapon carried one and no dice,
    /// and this fallback was what a weapon's damage was built on - 1-2, scaled. The multiplier is
    /// gone and the fallback means what it says, which makes a dice-less weapon a content bug that
    /// <c>BundleValidator</c> reports rather than a shape the engine expects.
    /// </remarks>
    [Fact]
    public void A_weapon_declaring_no_dice_swings_as_a_fist()
    {
        var stick = new ItemInstance
        {
            TemplateKey = "stick",
            EquippedSlot = ItemSlot.MainHand,
            ResolvedStats = new Dictionary<string, object> { { "bonus", 1 } },
        };

        var stats = EquipmentResolver.ResolveAttackerStats(level: 1, mightModifier: 2, equipped: new[] { stick });

        Assert.Equal(1, stats.MinDamage);
        Assert.Equal(2, stats.MaxDamage);

        // The flat modifier is untouched by any of this - a strong character still hits harder.
        Assert.Equal(2, stats.BaseDamage);
    }

    /// <summary>A weapon's dice are what it hits for, with nothing applied on top.</summary>
    [Fact]
    public void A_weapon_hits_for_the_dice_it_declares()
    {
        var axe = new ItemInstance
        {
            TemplateKey = "great-axe",
            EquippedSlot = ItemSlot.MainHand,
            ResolvedStats = new Dictionary<string, object> { { "damageMin", 3 }, { "damageMax", 8 } },
        };

        var stats = EquipmentResolver.ResolveAttackerStats(level: 1, mightModifier: 0, equipped: new[] { axe });

        Assert.Equal(3, stats.MinDamage);
        Assert.Equal(8, stats.MaxDamage);
    }

    /// <summary>
    /// <c>damageMultiplier</c> is retired, and a weapon still carrying one is not half-read.
    /// </summary>
    /// <remarks>
    /// The same guarantee the armour rework's casualties get below, and it matters more here: every
    /// weapon in the game carried one until the dice landed, so an old row is the likely case rather
    /// than the odd one. A weapon with a multiplier and dice hits for its dice; a weapon with a
    /// multiplier and no dice hits like a fist. Silently scaling either would be the old behaviour
    /// arriving back through a database rather than through the code.
    /// </remarks>
    [Fact]
    public void A_retired_damage_multiplier_is_ignored_rather_than_applied()
    {
        var withDice = new ItemInstance
        {
            TemplateKey = "sword",
            EquippedSlot = ItemSlot.MainHand,
            ResolvedStats = new Dictionary<string, object>
            {
                { "damageMin", 4 },
                { "damageMax", 9 },
                { "damageMultiplier", 4 },
            },
        };

        var stats = EquipmentResolver.ResolveAttackerStats(level: 1, mightModifier: 0, equipped: new[] { withDice });

        Assert.Equal(4, stats.MinDamage);
        Assert.Equal(9, stats.MaxDamage);

        var multiplierOnly = new ItemInstance
        {
            TemplateKey = "old-sword",
            EquippedSlot = ItemSlot.MainHand,
            ResolvedStats = new Dictionary<string, object> { { "damageMultiplier", 4 } },
        };

        var fists = EquipmentResolver.ResolveAttackerStats(
            level: 1, mightModifier: 0, equipped: new[] { multiplierOnly });

        Assert.Equal(1, fists.MinDamage);
        Assert.Equal(2, fists.MaxDamage);
    }

    /// <summary>
    /// Each hand rolls its own weapon and nothing else, so a main-hand swing cannot see what is in
    /// the off hand.
    /// </summary>
    /// <remarks>
    /// Load-bearing for dual wielding: each hand is its own attack on its own timer, and an earlier
    /// shape folded both hands' stats into one swing - right while there was only one swing to fold
    /// them into, and now it would count the same dagger twice.
    /// </remarks>
    [Fact]
    public void ResolveAttackerStatsForHand_reads_only_that_hands_weapon()
    {
        var main = new ItemInstance
        {
            TemplateKey = "sword",
            EquippedSlot = ItemSlot.MainHand,
            ResolvedStats = new Dictionary<string, object> { { "damageMin", 4 }, { "damageMax", 9 } },
        };

        var off = new ItemInstance
        {
            TemplateKey = "dagger",
            EquippedSlot = ItemSlot.OffHand,
            ResolvedStats = new Dictionary<string, object> { { "damageMin", 2 }, { "damageMax", 5 } },
        };

        var mainHand = EquipmentResolver.ResolveAttackerStatsForHand(
            level: 1, mightModifier: 0, equipped: new[] { main, off }, hand: ItemSlot.MainHand, offHandShare: 1m);

        Assert.Equal(4, mainHand.MinDamage);
        Assert.Equal(9, mainHand.MaxDamage);

        var offHand = EquipmentResolver.ResolveAttackerStatsForHand(
            level: 1, mightModifier: 0, equipped: new[] { main, off }, hand: ItemSlot.OffHand, offHandShare: 1m);

        Assert.Equal(2, offHand.MinDamage);
        Assert.Equal(5, offHand.MaxDamage);
    }

    [Fact]
    public void ResolveAttackerStats_defaults_to_the_main_hand()
    {
        var main = new ItemInstance
        {
            TemplateKey = "sword",
            EquippedSlot = ItemSlot.MainHand,
            ResolvedStats = new Dictionary<string, object> { { "damageMin", 4 }, { "damageMax", 9 } },
        };

        var byDefault = EquipmentResolver.ResolveAttackerStats(
            level: 1, mightModifier: 0, equipped: new[] { main });

        var named = EquipmentResolver.ResolveAttackerStatsForHand(
            level: 1, mightModifier: 0, equipped: new[] { main }, hand: ItemSlot.MainHand, offHandShare: 1m);

        Assert.Equal(named, byDefault);
    }

    /// <summary>
    /// jsonb round-trips hand these back as strings or <c>JsonElement</c>s rather than numbers, and
    /// dice that silently fail to parse look like a balance problem rather than a bug.
    /// </summary>
    [Fact]
    public void ResolveAttackerStats_reads_dice_stored_as_strings()
    {
        var sword = new ItemInstance
        {
            TemplateKey = "long-sword",
            EquippedSlot = ItemSlot.MainHand,
            ResolvedStats = new Dictionary<string, object> { { "damageMin", "4" }, { "damageMax", "8" } },
        };

        var stats = EquipmentResolver.ResolveAttackerStats(level: 1, mightModifier: 0, equipped: new[] { sword });

        Assert.Equal(4, stats.MinDamage);
        Assert.Equal(8, stats.MaxDamage);
    }

    /// <summary>Weapon dice on an armour piece are not a weapon.</summary>
    [Fact]
    public void ResolveAttackerStats_ignores_weapon_dice_on_armour()
    {
        var boots = new ItemInstance
        {
            TemplateKey = "boots",
            EquippedSlot = ItemSlot.Feet,
            ResolvedStats = new Dictionary<string, object> { { "damageMin", 10 }, { "damageMax", 20 } },
        };

        var stats = EquipmentResolver.ResolveAttackerStats(level: 1, mightModifier: 0, equipped: new[] { boots });

        Assert.Equal(1, stats.MinDamage);
        Assert.Equal(2, stats.MaxDamage);
    }

    [Fact]
    public void The_retired_armour_vocabulary_is_ignored_rather_than_half_read()
    {
        // armorFlat, armorPercent and armorMultiplier are gone (see ArmorCurve). A piece still
        // carrying them in an old database contributes nothing rather than something arbitrary -
        // which is the honest reading, since none of the three converts to a rating.
        var mail = new ItemInstance
        {
            TemplateKey = "chain-mail",
            EquippedSlot = ItemSlot.Chest,
            ResolvedStats = new Dictionary<string, object>
            {
                { "armorFlat", 4 },
                { "armorPercent", 0.5m },
                { "armorMultiplier", 2 },
            },
        };

        var stats = EquipmentResolver.ResolveDefenderStats(
            level: 1,
            agilityModifier: 0,
            equippedArmor: new[] { mail });

        Assert.Equal(0, stats.Armor);
    }
}
