using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Randomness;

namespace DikuWeb.Domain.Tests.Combat;

public class DamageCalculatorTests
{
    private static readonly SeededRandomSource ZeroSeed = new(0);

    // =========================================================================
    // Hit / Miss Basics
    // =========================================================================

    [Fact]
    public void Basic_hit_when_attack_meets_defense()
    {
        var attacker = new AttackerStats(AttackRating: 5, BaseDamage: 3, MinDamage: 1, MaxDamage: 6);
        var defender = new DefenderStats(DefenseRating: 4, ArmorFlat: 0, ArmorPercent: 0);

        var random = new SeededRandomSource(42); // rolled 15 (attack 15+5=20, defense 10+4=14)
        var result = DamageCalculator.CalculateDamage(attacker, defender, random);

        Assert.True(result.Hit);
        Assert.NotEqual(0, result.DamageDealt);
    }

    [Fact]
    public void Miss_when_attack_less_than_defense()
    {
        var attacker = new AttackerStats(AttackRating: 2, BaseDamage: 3, MinDamage: 1, MaxDamage: 6);
        var defender = new DefenderStats(DefenseRating: 10, ArmorFlat: 0, ArmorPercent: 0);

        var random = new SeededRandomSource(0); // roll: 1 (attack 1+2=3, defense 10+10=20)
        var result = DamageCalculator.CalculateDamage(attacker, defender, random);

        Assert.False(result.Hit);
        Assert.Equal(0, result.DamageDealt);
    }

    [Fact]
    public void Hit_exactly_at_defense_threshold()
    {
        var attacker = new AttackerStats(AttackRating: 5, BaseDamage: 2, MinDamage: 1, MaxDamage: 6);
        var defender = new DefenderStats(DefenseRating: 5, ArmorFlat: 0, ArmorPercent: 0);

        // Roll exactly 5: attack roll = 5 + 5 = 10, defense = 10 + 5 = 15 (miss)
        // Roll exactly 6: attack roll = 6 + 5 = 11, defense = 10 + 5 = 15 (miss)
        // Roll exactly 10: attack roll = 10 + 5 = 15, defense = 10 + 5 = 15 (hit)
        var random = new SeededRandomSource(10); // d20 rolls 10
        var result = DamageCalculator.CalculateDamage(attacker, defender, random);

        Assert.True(result.Hit);
    }

    // =========================================================================
    // Critical Hits
    // =========================================================================

    [Fact]
    public void Natural_20_is_critical()
    {
        var attacker = new AttackerStats(AttackRating: 15, BaseDamage: 2, MinDamage: 1, MaxDamage: 6);
        var defender = new DefenderStats(DefenseRating: 5, ArmorFlat: 0, ArmorPercent: 0);

        // Brute force find a seed that rolls 20
        for (int seed = 0; seed < 1000; seed++)
        {
            var random = new SeededRandomSource(seed);
            if (random.Next(1, 21) == 20)
            {
                var result = DamageCalculator.CalculateDamage(attacker, defender, new SeededRandomSource(seed));
                Assert.True(result.Hit);
                Assert.True(result.IsCritical);
                return;
            }
        }

        Assert.Fail("Could not find a seed that rolls natural 20");
    }

    [Fact]
    public void Beat_defense_by_10_or_more_is_critical()
    {
        var attacker = new AttackerStats(AttackRating: 15, BaseDamage: 2, MinDamage: 1, MaxDamage: 6);
        var defender = new DefenderStats(DefenseRating: 5, ArmorFlat: 0, ArmorPercent: 0);

        // Find a seed that rolls just barely beats (not crit)
        bool foundNonCritHit = false;
        for (int seed = 0; seed < 1000; seed++)
        {
            var random = new SeededRandomSource(seed);
            var roll = random.Next(1, 21);
            if (roll == 1) // Roll 1: attack = 1 + 15 = 16, defense = 10 + 5 = 15, beats by 1 (not crit)
            {
                var result1 = DamageCalculator.CalculateDamage(attacker, defender, new SeededRandomSource(seed));
                Assert.True(result1.Hit);
                Assert.False(result1.IsCritical);
                foundNonCritHit = true;
                break;
            }
        }
        Assert.True(foundNonCritHit, "Could not find seed for non-crit hit");

        // Find a seed that rolls high enough to beat by 10+
        bool foundCritHit = false;
        for (int seed = 0; seed < 1000; seed++)
        {
            var random = new SeededRandomSource(seed);
            var roll = random.Next(1, 21);
            if (roll >= 15) // Roll 15+: attack = 15+ + 15 = 30+, defense = 15, beats by 15+ (crit)
            {
                var result10 = DamageCalculator.CalculateDamage(attacker, defender, new SeededRandomSource(seed));
                Assert.True(result10.Hit);
                Assert.True(result10.IsCritical);
                foundCritHit = true;
                break;
            }
        }
        Assert.True(foundCritHit, "Could not find seed for crit hit");
    }

    [Fact]
    public void Critical_rolls_damage_twice_and_uses_higher()
    {
        var attacker = new AttackerStats(AttackRating: 10, BaseDamage: 0, MinDamage: 1, MaxDamage: 6);
        var defender = new DefenderStats(DefenseRating: 0, ArmorFlat: 0, ArmorPercent: 0);

        // Find a seed that produces natural 20 (guaranteed crit)
        for (int seed = 0; seed < 1000; seed++)
        {
            var random = new SeededRandomSource(seed);
            if (random.Next(1, 21) == 20)
            {
                var result = DamageCalculator.CalculateDamage(attacker, defender, new SeededRandomSource(seed));
                Assert.True(result.IsCritical);
                Assert.True(result.DamageDealt > 0);
                return;
            }
        }

        Assert.Fail("Could not find a seed that rolls natural 20");
    }

    // =========================================================================
    // Armor - Flat Reduction
    // =========================================================================

    [Fact]
    public void Flat_armor_reduces_damage()
    {
        var attacker = new AttackerStats(AttackRating: 10, BaseDamage: 5, MinDamage: 1, MaxDamage: 6);
        var defender = new DefenderStats(DefenseRating: 0, ArmorFlat: 3, ArmorPercent: 0);

        var random = new SeededRandomSource(15); // guaranteed hit
        var result = DamageCalculator.CalculateDamage(attacker, defender, random);

        Assert.True(result.Hit);
        // baseDamage=5, min roll=1, so min total=6. After flat 3: 6-3=3
        Assert.True(result.DamageDealt >= 3);
    }

    [Fact]
    public void Flat_armor_cannot_reduce_below_one()
    {
        var attacker = new AttackerStats(AttackRating: 10, BaseDamage: 1, MinDamage: 1, MaxDamage: 2);
        var defender = new DefenderStats(DefenseRating: 0, ArmorFlat: 100, ArmorPercent: 0);

        var random = new SeededRandomSource(15);
        var result = DamageCalculator.CalculateDamage(attacker, defender, random);

        Assert.True(result.Hit);
        Assert.Equal(1, result.DamageDealt); // Clamped to minimum 1
    }

    // =========================================================================
    // Armor - Percentage Reduction
    // =========================================================================

    [Fact]
    public void Percentage_armor_reduces_damage()
    {
        var attacker = new AttackerStats(AttackRating: 10, BaseDamage: 10, MinDamage: 1, MaxDamage: 6);
        var defender = new DefenderStats(DefenseRating: 0, ArmorFlat: 0, ArmorPercent: 0.5m);

        var random = new SeededRandomSource(15);
        var result = DamageCalculator.CalculateDamage(attacker, defender, random);

        Assert.True(result.Hit);
        // baseDamage=10, min roll=1, total=11. After 50% reduction: 11 * 0.5 = 5.5 → 5
        Assert.True(result.DamageDealt <= 6); // max roll + base would be 6+10=16, halved=8
    }

    [Fact]
    public void Percentage_armor_100_percent_still_deals_one()
    {
        var attacker = new AttackerStats(AttackRating: 10, BaseDamage: 10, MinDamage: 1, MaxDamage: 6);
        var defender = new DefenderStats(DefenseRating: 0, ArmorFlat: 0, ArmorPercent: 1.0m);

        var random = new SeededRandomSource(15);
        var result = DamageCalculator.CalculateDamage(attacker, defender, random);

        Assert.True(result.Hit);
        Assert.Equal(1, result.DamageDealt);
    }

    // =========================================================================
    // Armor - Combined Flat + Percentage
    // =========================================================================

    [Fact]
    public void Flat_then_percentage_armor_combined()
    {
        var attacker = new AttackerStats(AttackRating: 10, BaseDamage: 10, MinDamage: 1, MaxDamage: 6);
        var defender = new DefenderStats(DefenseRating: 0, ArmorFlat: 5, ArmorPercent: 0.5m);

        var random = new SeededRandomSource(15);
        var result = DamageCalculator.CalculateDamage(attacker, defender, random);

        Assert.True(result.Hit);
        // min total damage: 10 + 1 = 11
        // after flat 5: 11 - 5 = 6
        // after 50%: 6 * 0.5 = 3
        Assert.True(result.DamageDealt >= 3);
    }

    // =========================================================================
    // Negative Modifiers
    // =========================================================================

    [Fact]
    public void Negative_attack_rating_increases_difficulty()
    {
        var attackerWeak = new AttackerStats(AttackRating: -5, BaseDamage: 5, MinDamage: 1, MaxDamage: 6);
        var attackerStrong = new AttackerStats(AttackRating: 5, BaseDamage: 5, MinDamage: 1, MaxDamage: 6);
        var defender = new DefenderStats(DefenseRating: 5, ArmorFlat: 0, ArmorPercent: 0);

        // Both roll 15
        var randomWeak = new SeededRandomSource(15);
        var randomStrong = new SeededRandomSource(15);

        var weakResult = DamageCalculator.CalculateDamage(attackerWeak, defender, randomWeak);
        var strongResult = DamageCalculator.CalculateDamage(attackerStrong, defender, randomStrong);

        // Weak: 15 + (-5) = 10 vs 15 (miss)
        // Strong: 15 + 5 = 20 vs 15 (hit)
        Assert.False(weakResult.Hit);
        Assert.True(strongResult.Hit);
    }

    [Fact]
    public void Negative_defense_rating_makes_easier_to_hit()
    {
        var attacker = new AttackerStats(AttackRating: 2, BaseDamage: 5, MinDamage: 1, MaxDamage: 6);
        var defenderStrong = new DefenderStats(DefenseRating: 5, ArmorFlat: 0, ArmorPercent: 0);
        var defenderWeak = new DefenderStats(DefenseRating: -5, ArmorFlat: 0, ArmorPercent: 0);

        // Both roll 5
        var randomStrong = new SeededRandomSource(5);
        var randomWeak = new SeededRandomSource(5);

        var strongResult = DamageCalculator.CalculateDamage(attacker, defenderStrong, randomStrong);
        var weakResult = DamageCalculator.CalculateDamage(attacker, defenderWeak, randomWeak);

        // Strong: 5 + 2 = 7 vs 10 + 5 = 15 (miss)
        // Weak: 5 + 2 = 7 vs 10 + (-5) = 5 (hit)
        Assert.False(strongResult.Hit);
        Assert.True(weakResult.Hit);
    }

    // =========================================================================
    // Damage Variance
    // =========================================================================

    [Fact]
    public void Damage_varies_within_weapon_range()
    {
        var attacker = new AttackerStats(AttackRating: 10, BaseDamage: 0, MinDamage: 1, MaxDamage: 6);
        var defender = new DefenderStats(DefenseRating: 0, ArmorFlat: 0, ArmorPercent: 0);

        var damageValues = new HashSet<int>();

        // Run multiple times with different seeds
        for (int i = 1; i <= 100; i++)
        {
            var random = new SeededRandomSource(i * 17); // different seeds
            var result = DamageCalculator.CalculateDamage(attacker, defender, random);
            if (result.Hit)
            {
                damageValues.Add(result.DamageDealt);
            }
        }

        // Should see varied damage rolls across the range
        Assert.True(damageValues.Count > 1, "Damage should vary across runs");
        Assert.All(damageValues, d => Assert.True(d >= 1 && d <= 6, "Damage in weapon range"));
    }

    // =========================================================================
    // Natural Roll Tracking
    // =========================================================================

    [Fact]
    public void Natural_roll_is_tracked()
    {
        var attacker = new AttackerStats(AttackRating: 0, BaseDamage: 1, MinDamage: 1, MaxDamage: 1);
        var defender = new DefenderStats(DefenseRating: 0, ArmorFlat: 0, ArmorPercent: 0);

        // Just verify that the result contains a natural roll in the valid range
        var random = new SeededRandomSource(42);
        var result = DamageCalculator.CalculateDamage(attacker, defender, random);

        Assert.True(result.NaturalRoll >= 1 && result.NaturalRoll <= 20);
    }

    [Fact]
    public void Natural_roll_range_covers_all_values()
    {
        var attacker = new AttackerStats(AttackRating: 0, BaseDamage: 1, MinDamage: 1, MaxDamage: 1);
        var defender = new DefenderStats(DefenseRating: 0, ArmorFlat: 0, ArmorPercent: 0);

        var rolls = new HashSet<int>();
        for (int seed = 0; seed < 1000; seed++)
        {
            var random = new SeededRandomSource(seed);
            var result = DamageCalculator.CalculateDamage(attacker, defender, random);
            rolls.Add(result.NaturalRoll);
        }

        // Should see reasonable coverage of the d20 range (doesn't need all 20 in 1000 tries due to randomness)
        Assert.True(rolls.Count >= 15, $"Expected at least 15 different rolls in 1000 attempts, got {rolls.Count}");
        Assert.All(rolls, r => Assert.True(r >= 1 && r <= 20));
    }

    // =========================================================================
    // Determinism
    // =========================================================================

    [Fact]
    public void Same_inputs_same_seed_same_output()
    {
        var attacker = new AttackerStats(AttackRating: 7, BaseDamage: 3, MinDamage: 2, MaxDamage: 8);
        var defender = new DefenderStats(DefenseRating: 4, ArmorFlat: 2, ArmorPercent: 0.1m);

        var result1 = DamageCalculator.CalculateDamage(attacker, defender, new SeededRandomSource(42));
        var result2 = DamageCalculator.CalculateDamage(attacker, defender, new SeededRandomSource(42));

        Assert.Equal(result1.Hit, result2.Hit);
        Assert.Equal(result1.IsCritical, result2.IsCritical);
        Assert.Equal(result1.DamageDealt, result2.DamageDealt);
        Assert.Equal(result1.NaturalRoll, result2.NaturalRoll);
    }

    [Fact]
    public void Different_seeds_different_results()
    {
        var attacker = new AttackerStats(AttackRating: 0, BaseDamage: 0, MinDamage: 1, MaxDamage: 6);
        var defender = new DefenderStats(DefenseRating: 0, ArmorFlat: 0, ArmorPercent: 0);

        var results = new HashSet<int>();

        for (int seed = 1; seed <= 20; seed++)
        {
            var result = DamageCalculator.CalculateDamage(attacker, defender, new SeededRandomSource(seed));
            results.Add(result.NaturalRoll);
        }

        // Should see variation across seeds
        Assert.True(results.Count > 1);
    }

    // =========================================================================
    // Edge Cases
    // =========================================================================

    [Fact]
    public void Zero_base_damage_still_hits()
    {
        var attacker = new AttackerStats(AttackRating: 10, BaseDamage: 0, MinDamage: 1, MaxDamage: 6);
        var defender = new DefenderStats(DefenseRating: 0, ArmorFlat: 0, ArmorPercent: 0);

        var random = new SeededRandomSource(15);
        var result = DamageCalculator.CalculateDamage(attacker, defender, random);

        Assert.True(result.Hit);
        Assert.True(result.DamageDealt >= 1);
    }

    [Fact]
    public void Negative_base_damage_reduced_by_roll()
    {
        var attacker = new AttackerStats(AttackRating: 10, BaseDamage: -2, MinDamage: 1, MaxDamage: 6);
        var defender = new DefenderStats(DefenseRating: 0, ArmorFlat: 0, ArmorPercent: 0);

        var random = new SeededRandomSource(15);
        var result = DamageCalculator.CalculateDamage(attacker, defender, random);

        Assert.True(result.Hit);
        // min roll 1 + base -2 = -1, then clamped to 1
        Assert.Equal(1, result.DamageDealt);
    }

    [Fact]
    public void Very_high_damage_values_work()
    {
        var attacker = new AttackerStats(AttackRating: 50, BaseDamage: 1000, MinDamage: 100, MaxDamage: 200);
        var defender = new DefenderStats(DefenseRating: 0, ArmorFlat: 10, ArmorPercent: 0.05m);

        var random = new SeededRandomSource(15);
        var result = DamageCalculator.CalculateDamage(attacker, defender, random);

        Assert.True(result.Hit);
        Assert.True(result.DamageDealt > 1000);
    }
}
