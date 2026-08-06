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

        // Attack rating: 5/2 = 2 (integer division) + Might 2 = 4
        Assert.Equal(4, stats.AttackRating);
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

        // Attack rating: 5/2 + Might 1 + weapon bonus 3 = 2 + 1 + 3 = 6
        Assert.Equal(6, stats.AttackRating);
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

        Assert.Equal(unarmed.AttackRating + 2, armed.AttackRating);
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

        // Level difference is 2, so attack rating difference is 1
        Assert.Equal(1, high.AttackRating - low.AttackRating);
    }

    [Fact]
    public void ResolveDefenderStats_unarmored_uses_agility_only()
    {
        var stats = EquipmentResolver.ResolveDefenderStats(
            agilityModifier: 2,
            equippedArmor: []);

        // Defense rating: 10 + Agility 2 = 12 (but 10 is baked in the formula, we just add modifier)
        Assert.Equal(2, stats.DefenseRating);
        Assert.Equal(0, stats.ArmorFlat);
        Assert.Equal(0m, stats.ArmorPercent);
    }

    [Fact]
    public void ResolveDefenderStats_armor_stacks_flat()
    {
        var chest = new ItemInstance
        {
            TemplateKey = "iron-chest",
            EquippedSlot = ItemSlot.Chest,
            ResolvedStats = new Dictionary<string, object> { { "armorFlat", 3 } }
        };

        var legs = new ItemInstance
        {
            TemplateKey = "iron-legs",
            EquippedSlot = ItemSlot.Legs,
            ResolvedStats = new Dictionary<string, object> { { "armorFlat", 2 } }
        };

        var stats = EquipmentResolver.ResolveDefenderStats(
            agilityModifier: 1,
            equippedArmor: new[] { chest, legs });

        Assert.Equal(1, stats.DefenseRating);
        Assert.Equal(5, stats.ArmorFlat);
        Assert.Equal(0m, stats.ArmorPercent);
    }

    [Fact]
    public void ResolveDefenderStats_armor_stacks_percent()
    {
        var chest = new ItemInstance
        {
            TemplateKey = "leather-chest",
            EquippedSlot = ItemSlot.Chest,
            ResolvedStats = new Dictionary<string, object> { { "armorPercent", 0.1m } }
        };

        var legs = new ItemInstance
        {
            TemplateKey = "leather-legs",
            EquippedSlot = ItemSlot.Legs,
            ResolvedStats = new Dictionary<string, object> { { "armorPercent", 0.05m } }
        };

        var stats = EquipmentResolver.ResolveDefenderStats(
            agilityModifier: 0,
            equippedArmor: new[] { chest, legs });

        Assert.Equal(0, stats.DefenseRating);
        Assert.Equal(0, stats.ArmorFlat);
        Assert.Equal(0.15m, stats.ArmorPercent);
    }

    [Fact]
    public void ResolveDefenderStats_armor_percent_clamped_to_95percent()
    {
        var gear = Enumerable.Range(0, 20)
            .Select(i => new ItemInstance
            {
                TemplateKey = $"gear-{i}",
                EquippedSlot = ItemSlot.Head,
                ResolvedStats = new Dictionary<string, object> { { "armorPercent", 0.1m } }
            })
            .ToList();

        var stats = EquipmentResolver.ResolveDefenderStats(
            agilityModifier: 0,
            equippedArmor: gear);

        Assert.Equal(0.95m, stats.ArmorPercent);
    }

    [Fact]
    public void ResolveDefenderStats_armor_defense_bonus()
    {
        var armor = new ItemInstance
        {
            TemplateKey = "blessed-mail",
            EquippedSlot = ItemSlot.Chest,
            ResolvedStats = new Dictionary<string, object> { { "defense", 2 } }
        };

        var stats = EquipmentResolver.ResolveDefenderStats(
            agilityModifier: 1,
            equippedArmor: new[] { armor });

        Assert.Equal(3, stats.DefenseRating);
    }

    [Fact]
    public void ResolveDefenderStats_ignores_non_armor_slots()
    {
        var weapon = new ItemInstance
        {
            TemplateKey = "sword",
            EquippedSlot = ItemSlot.MainHand,
            ResolvedStats = new Dictionary<string, object> { { "armorFlat", 10 } }
        };

        var trinket = new ItemInstance
        {
            TemplateKey = "ring",
            EquippedSlot = ItemSlot.Trinket,
            ResolvedStats = new Dictionary<string, object> { { "armorFlat", 10 } }
        };

        var stats = EquipmentResolver.ResolveDefenderStats(
            agilityModifier: 0,
            equippedArmor: new[] { weapon, trinket });

        // Neither main-hand nor trinket should contribute armor
        Assert.Equal(0, stats.ArmorFlat);
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
            agilityModifier: 1,
            equippedArmor: new[] { armor });

        Assert.Equal(1, stats.DefenseRating);
        Assert.Equal(0, stats.ArmorFlat);
    }

    [Fact]
    public void ResolveDefenderStats_mixed_armor_and_defense()
    {
        var chest = new ItemInstance
        {
            TemplateKey = "iron-chest",
            EquippedSlot = ItemSlot.Chest,
            ResolvedStats = new Dictionary<string, object>
            {
                { "armorFlat", 5 },
                { "defense", 1 }
            }
        };

        var legs = new ItemInstance
        {
            TemplateKey = "leather-legs",
            EquippedSlot = ItemSlot.Legs,
            ResolvedStats = new Dictionary<string, object>
            {
                { "armorPercent", 0.1m },
                { "defense", 1 }
            }
        };

        var stats = EquipmentResolver.ResolveDefenderStats(
            agilityModifier: 2,
            equippedArmor: new[] { chest, legs });

        Assert.Equal(4, stats.DefenseRating); // 2 agility + 1 + 1 defense
        Assert.Equal(5, stats.ArmorFlat);
        Assert.Equal(0.1m, stats.ArmorPercent);
    }

    [Fact]
    public void ResolveAttackerStats_negative_modifiers()
    {
        var stats = EquipmentResolver.ResolveAttackerStats(
            level: 8,
            mightModifier: -2,
            equippedMainHand: null);

        // Attack rating: 8/2 + (-2) = 4 - 2 = 2
        Assert.Equal(2, stats.AttackRating);
        // Base damage: -2
        Assert.Equal(-2, stats.BaseDamage);
    }

    [Fact]
    public void ResolveDefenderStats_negative_agility()
    {
        var stats = EquipmentResolver.ResolveDefenderStats(
            agilityModifier: -3,
            equippedArmor: []);

        Assert.Equal(-3, stats.DefenseRating);
    }

    [Fact]
    public void ResolveDefenderStats_armor_percent_negative_clamped_to_zero()
    {
        var armor = new ItemInstance
        {
            TemplateKey = "cursed-mail",
            EquippedSlot = ItemSlot.Chest,
            ResolvedStats = new Dictionary<string, object> { { "armorPercent", -0.5m } }
        };

        var stats = EquipmentResolver.ResolveDefenderStats(
            agilityModifier: 0,
            equippedArmor: new[] { armor });

        // Clamp to 0-95% range
        Assert.Equal(0m, stats.ArmorPercent);
    }
}
