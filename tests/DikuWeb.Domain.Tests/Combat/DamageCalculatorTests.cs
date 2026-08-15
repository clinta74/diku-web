using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Randomness;

namespace DikuWeb.Domain.Tests.Combat;

public class DamageCalculatorTests
{
    /// <summary>
    /// A defender at level 0, so <c>defenseVal</c> is <c>10 + DefenseRating</c> and a test about
    /// the roll is not also a test about the level term.
    /// </summary>
    private static DefenderStats Defender(int defenseRating = 0, int armor = 0) =>
        new(Level: 0, DefenseRating: defenseRating, Armor: armor);

    /// <summary>A seed whose first d20 is the face asked for.</summary>
    private static SeededRandomSource RollOf(int face)
    {
        for (var seed = 0; seed < 100_000; seed++)
        {
            if (new SeededRandomSource(seed).Next(1, 21) == face)
            {
                return new SeededRandomSource(seed);
            }
        }

        throw new InvalidOperationException($"No seed under 100000 rolls a {face}.");
    }

    // =========================================================================
    // Hit / Miss Basics
    // =========================================================================

    [Fact]
    public void Basic_hit_when_attack_meets_defense()
    {
        var attacker = new AttackerStats(AttackRating: 5, BaseDamage: 3, MinDamage: 1, MaxDamage: 6);

        // defenseVal 14, so a 9 or better lands. Rolling 15.
        var result = DamageCalculator.CalculateDamage(attacker, Defender(defenseRating: 4), RollOf(15));

        Assert.True(result.Hit);
        Assert.NotEqual(0, result.DamageDealt);
    }

    [Fact]
    public void Miss_when_attack_less_than_defense()
    {
        var attacker = new AttackerStats(AttackRating: 2, BaseDamage: 3, MinDamage: 1, MaxDamage: 6);

        // defenseVal 20, so an 18 or better lands. Rolling 3.
        var result = DamageCalculator.CalculateDamage(attacker, Defender(defenseRating: 10), RollOf(3));

        Assert.False(result.Hit);
        Assert.Equal(0, result.DamageDealt);
    }

    [Fact]
    public void Hit_exactly_at_defense_threshold()
    {
        var attacker = new AttackerStats(AttackRating: 5, BaseDamage: 2, MinDamage: 1, MaxDamage: 6);

        // defenseVal 15, attackRating 5, so exactly 10 is needed.
        Assert.False(DamageCalculator.CalculateDamage(attacker, Defender(defenseRating: 5), RollOf(9)).Hit);
        Assert.True(DamageCalculator.CalculateDamage(attacker, Defender(defenseRating: 5), RollOf(10)).Hit);
    }

    // =========================================================================
    // The two ends are always open (PLAN.md §4.6)
    // =========================================================================

    [Fact]
    public void A_natural_1_always_misses_however_outmatched_the_defender_is()
    {
        // Nothing may be authored into certainty. Without the clamp this attacker needs a -30 and
        // would land every blow it ever threw.
        var overwhelming = new AttackerStats(AttackRating: 500, BaseDamage: 5, MinDamage: 1, MaxDamage: 6);

        var result = DamageCalculator.CalculateDamage(overwhelming, Defender(), RollOf(1));

        Assert.False(result.Hit);
    }

    [Fact]
    public void A_natural_20_always_hits_however_defended_the_defender_is()
    {
        var hopeless = new AttackerStats(AttackRating: -50, BaseDamage: 5, MinDamage: 1, MaxDamage: 6);
        var fortress = new DefenderStats(Level: 50, DefenseRating: 500, Armor: 100_000);

        var result = DamageCalculator.CalculateDamage(hopeless, fortress, RollOf(20));

        Assert.True(result.Hit);
        Assert.True(result.DamageDealt >= 1);
    }

    [Fact]
    public void No_defender_can_be_missed_more_than_ninety_five_percent_of_the_time()
    {
        var hopeless = new AttackerStats(AttackRating: 0, BaseDamage: 1, MinDamage: 1, MaxDamage: 1);
        var fortress = new DefenderStats(Level: 50, DefenseRating: 900, Armor: 0);

        var hits = 0;
        for (var seed = 0; seed < 4000; seed++)
        {
            if (DamageCalculator.CalculateDamage(hopeless, fortress, new SeededRandomSource(seed)).Hit)
            {
                hits++;
            }
        }

        // Exactly the natural 20s, which is one face in twenty.
        Assert.InRange(hits / 4000d, 0.02, 0.08);
    }

    // =========================================================================
    // Level cancels, which is what keeps the die deciding (PLAN.md §4.6)
    // =========================================================================

    [Fact]
    public void An_even_matchup_needs_the_same_roll_at_every_level()
    {
        // Attack rating and the number to beat both carry level/2, so two evenly matched
        // combatants face the same die at level 2 and at level 50. Before this, the gap widened
        // by level/4 until the d20 could no longer express it and everything hit everything.
        int? needed = null;

        foreach (var level in new[] { 2, 10, 20, 30, 40, 50 })
        {
            var attacker = new AttackerStats(
                AttackRating: level / 2, BaseDamage: 0, MinDamage: 1, MaxDamage: 1);
            var defender = new DefenderStats(Level: level, DefenseRating: 0, Armor: 0);

            var lowest = Enumerable.Range(1, 20).First(face =>
                DamageCalculator.CalculateDamage(attacker, defender, RollOf(face)).Hit);

            needed ??= lowest;
            Assert.Equal(needed, lowest);
        }
    }

    [Fact]
    public void A_higher_level_defender_is_harder_to_hit()
    {
        // The level term cancelling at parity must not mean level stops mattering across it.
        var attacker = new AttackerStats(AttackRating: 5, BaseDamage: 0, MinDamage: 1, MaxDamage: 1);

        var lowLevel = Enumerable.Range(1, 20).First(face => DamageCalculator
            .CalculateDamage(attacker, new DefenderStats(10, 0, 0), RollOf(face)).Hit);
        var highLevel = Enumerable.Range(1, 20).First(face => DamageCalculator
            .CalculateDamage(attacker, new DefenderStats(40, 0, 0), RollOf(face)).Hit);

        Assert.True(highLevel > lowLevel, $"needed {highLevel} at level 40 vs {lowLevel} at level 10");
    }

    // =========================================================================
    // Critical Hits
    // =========================================================================

    [Fact]
    public void Natural_20_is_critical()
    {
        var attacker = new AttackerStats(AttackRating: 15, BaseDamage: 2, MinDamage: 1, MaxDamage: 6);

        var result = DamageCalculator.CalculateDamage(attacker, Defender(defenseRating: 5), RollOf(20));

        Assert.True(result.Hit);
        Assert.True(result.IsCritical);
    }

    [Fact]
    public void Overshooting_the_defence_is_not_a_critical()
    {
        // A crit used to also trigger on beating the defence by ten or more. Overshoot grows with
        // the level gap without bound, so at level 50 every landed mob blow was a critical and the
        // dice were rolled twice on all of them. Only the face counts now.
        var overwhelming = new AttackerStats(AttackRating: 60, BaseDamage: 2, MinDamage: 1, MaxDamage: 6);

        foreach (var face in new[] { 1, 7, 12, 19 })
        {
            var result = DamageCalculator.CalculateDamage(overwhelming, Defender(), RollOf(face));
            Assert.False(result.IsCritical, $"natural {face} beat the defence by 50 and must not crit");
        }
    }

    [Fact]
    public void Critical_hits_sum_both_dice_rather_than_taking_the_better()
    {
        // Fixed dice make the rule visible: with a 4-4 weapon and no modifier, a crit must deal 8.
        var attacker = new AttackerStats(AttackRating: 10, BaseDamage: 0, MinDamage: 4, MaxDamage: 4);

        var result = DamageCalculator.CalculateDamage(attacker, Defender(), RollOf(20));

        Assert.True(result.IsCritical);
        Assert.Equal(8, result.DamageDealt);
    }

    [Fact]
    public void The_flat_modifier_is_added_once_on_a_crit_not_twice()
    {
        // Dice twice, modifier once: a 4-4 weapon with +3 Might crits for 4 + 4 + 3.
        var attacker = new AttackerStats(AttackRating: 10, BaseDamage: 3, MinDamage: 4, MaxDamage: 4);

        var result = DamageCalculator.CalculateDamage(attacker, Defender(), RollOf(20));

        Assert.True(result.IsCritical);
        Assert.Equal(11, result.DamageDealt);
    }

    // =========================================================================
    // Armor absorbs a fraction (ArmorCurve)
    // =========================================================================

    [Fact]
    public void Armor_reduces_damage()
    {
        var attacker = new AttackerStats(AttackRating: 10, BaseDamage: 40, MinDamage: 1, MaxDamage: 6);

        // Same face both times, so the dice are identical and armour is the only difference.
        var armoured = DamageCalculator.CalculateDamage(attacker, Defender(armor: 100), RollOf(15));
        var bare = DamageCalculator.CalculateDamage(attacker, Defender(), RollOf(15));

        Assert.True(armoured.Hit);
        Assert.True(bare.Hit);
        Assert.True(
            armoured.DamageDealt < bare.DamageDealt,
            $"armour should reduce damage: {armoured.DamageDealt} vs {bare.DamageDealt}");
    }

    [Fact]
    public void Armor_at_the_midpoint_halves_the_blow()
    {
        // The one sentence the whole curve is tuned against: a rating equal to Midpoint absorbs
        // half. Fixed dice so the assertion is arithmetic rather than a range.
        var attacker = new AttackerStats(AttackRating: 30, BaseDamage: 0, MinDamage: 40, MaxDamage: 40);

        var result = DamageCalculator.CalculateDamage(
            attacker, Defender(armor: ArmorCurve.Midpoint), RollOf(10));

        Assert.True(result.Hit);
        Assert.Equal(20, result.DamageDealt);
    }

    [Fact]
    public void No_amount_of_armor_reaches_immunity()
    {
        // The property the old flat subtraction could not have: absurd input, bounded output.
        var attacker = new AttackerStats(AttackRating: 30, BaseDamage: 0, MinDamage: 400, MaxDamage: 400);

        var result = DamageCalculator.CalculateDamage(attacker, Defender(armor: 100_000_000), RollOf(10));

        Assert.True(result.Hit);

        // The cap binds long before the curve does, so a quarter of the blow always lands.
        Assert.Equal(100, result.DamageDealt);
    }

    [Fact]
    public void Armor_cannot_reduce_a_landed_blow_below_one()
    {
        var attacker = new AttackerStats(AttackRating: 10, BaseDamage: 0, MinDamage: 1, MaxDamage: 1);

        var result = DamageCalculator.CalculateDamage(attacker, Defender(armor: 10_000), RollOf(10));

        Assert.True(result.Hit);
        Assert.Equal(1, result.DamageDealt);
    }

    [Fact]
    public void Negative_armor_absorbs_nothing_rather_than_amplifying()
    {
        var attacker = new AttackerStats(AttackRating: 10, BaseDamage: 0, MinDamage: 20, MaxDamage: 20);

        var negative = DamageCalculator.CalculateDamage(attacker, Defender(armor: -500), RollOf(10));
        var none = DamageCalculator.CalculateDamage(attacker, Defender(armor: 0), RollOf(10));

        Assert.Equal(none.DamageDealt, negative.DamageDealt);
    }

    // =========================================================================
    // Negative Modifiers
    // =========================================================================

    [Fact]
    public void Negative_attack_rating_increases_difficulty()
    {
        var weak = new AttackerStats(AttackRating: -5, BaseDamage: 5, MinDamage: 1, MaxDamage: 6);
        var strong = new AttackerStats(AttackRating: 5, BaseDamage: 5, MinDamage: 1, MaxDamage: 6);

        // defenseVal 15: the weak one needs a 20, the strong one a 10. Both roll 15.
        Assert.False(DamageCalculator.CalculateDamage(weak, Defender(defenseRating: 5), RollOf(15)).Hit);
        Assert.True(DamageCalculator.CalculateDamage(strong, Defender(defenseRating: 5), RollOf(15)).Hit);
    }

    [Fact]
    public void Negative_defense_rating_makes_easier_to_hit()
    {
        var attacker = new AttackerStats(AttackRating: 2, BaseDamage: 5, MinDamage: 1, MaxDamage: 6);

        Assert.False(DamageCalculator.CalculateDamage(attacker, Defender(defenseRating: 5), RollOf(5)).Hit);
        Assert.True(DamageCalculator.CalculateDamage(attacker, Defender(defenseRating: -5), RollOf(5)).Hit);
    }

    // =========================================================================
    // Damage Variance
    // =========================================================================

    [Fact]
    public void Damage_varies_within_weapon_range()
    {
        var attacker = new AttackerStats(AttackRating: 10, BaseDamage: 0, MinDamage: 1, MaxDamage: 6);

        var damageValues = new HashSet<int>();

        for (var i = 1; i <= 100; i++)
        {
            var result = DamageCalculator.CalculateDamage(attacker, Defender(), new SeededRandomSource(i * 17));
            if (result.Hit)
            {
                damageValues.Add(result.DamageDealt);
            }
        }

        Assert.True(damageValues.Count > 1, "Damage should vary across runs");

        // A crit sums two dice, so the reachable ceiling is two maximum faces rather than one.
        Assert.All(damageValues, d => Assert.True(d >= 1 && d <= 12, $"Damage {d} outside 1-12"));
    }

    // =========================================================================
    // Natural Roll Tracking
    // =========================================================================

    [Fact]
    public void Natural_roll_is_tracked()
    {
        var attacker = new AttackerStats(AttackRating: 0, BaseDamage: 1, MinDamage: 1, MaxDamage: 1);

        var result = DamageCalculator.CalculateDamage(attacker, Defender(), new SeededRandomSource(42));

        Assert.InRange(result.NaturalRoll, 1, 20);
    }

    [Fact]
    public void Natural_roll_range_covers_all_values()
    {
        var attacker = new AttackerStats(AttackRating: 0, BaseDamage: 1, MinDamage: 1, MaxDamage: 1);

        var rolls = new HashSet<int>();
        for (var seed = 0; seed < 1000; seed++)
        {
            rolls.Add(DamageCalculator
                .CalculateDamage(attacker, Defender(), new SeededRandomSource(seed)).NaturalRoll);
        }

        Assert.True(rolls.Count >= 15, $"Expected at least 15 different rolls in 1000 attempts, got {rolls.Count}");
        Assert.All(rolls, r => Assert.InRange(r, 1, 20));
    }

    // =========================================================================
    // Determinism
    // =========================================================================

    [Fact]
    public void Same_inputs_same_seed_same_output()
    {
        var attacker = new AttackerStats(AttackRating: 7, BaseDamage: 3, MinDamage: 2, MaxDamage: 8);
        var defender = new DefenderStats(Level: 12, DefenseRating: 4, Armor: 30);

        var first = DamageCalculator.CalculateDamage(attacker, defender, new SeededRandomSource(42));
        var second = DamageCalculator.CalculateDamage(attacker, defender, new SeededRandomSource(42));

        Assert.Equal(first, second);
    }

    [Fact]
    public void Different_seeds_different_results()
    {
        var attacker = new AttackerStats(AttackRating: 0, BaseDamage: 0, MinDamage: 1, MaxDamage: 6);

        var results = new HashSet<int>();
        for (var seed = 1; seed <= 20; seed++)
        {
            results.Add(DamageCalculator
                .CalculateDamage(attacker, Defender(), new SeededRandomSource(seed)).NaturalRoll);
        }

        Assert.True(results.Count > 1);
    }

    // =========================================================================
    // Edge Cases
    // =========================================================================

    [Fact]
    public void Zero_base_damage_still_hits()
    {
        var attacker = new AttackerStats(AttackRating: 10, BaseDamage: 0, MinDamage: 1, MaxDamage: 6);

        var result = DamageCalculator.CalculateDamage(attacker, Defender(), RollOf(15));

        Assert.True(result.Hit);
        Assert.True(result.DamageDealt >= 1);
    }

    [Fact]
    public void Negative_base_damage_reduced_by_roll()
    {
        // Fixed 1-1 dice so the clamp is what is being tested: a hit gives 1 - 2 = -1, which floors.
        var attacker = new AttackerStats(AttackRating: 10, BaseDamage: -2, MinDamage: 1, MaxDamage: 1);

        var result = DamageCalculator.CalculateDamage(attacker, Defender(), RollOf(15));

        Assert.True(result.Hit);
        Assert.Equal(1, result.DamageDealt);
    }

    [Fact]
    public void Very_high_damage_values_work()
    {
        var attacker = new AttackerStats(
            AttackRating: 50, BaseDamage: 1000, MinDamage: 100, MaxDamage: 200);

        var result = DamageCalculator.CalculateDamage(attacker, Defender(armor: 10), RollOf(15));

        Assert.True(result.Hit);

        // 1100 at the very least, less the ~9% a rating of 10 absorbs.
        Assert.True(result.DamageDealt > 900, $"dealt {result.DamageDealt}");
    }

    // =========================================================================
    // Mob stats - template values when present, level-derived when absent
    // =========================================================================

    [Fact]
    public void Mob_with_no_combat_stats_falls_back_to_level_derived_values()
    {
        var mob = NewMob(level: 9, stats: new() { { "health", 40 } });

        var stats = DamageCalculator.StatsFrom(mob);

        // level / 2, plus the baseline that stands for competence rather than level. Without it
        // the level term cancels against the defence and no mob can reach a geared character.
        Assert.Equal(10, stats.AttackRating);
        Assert.Equal(3, stats.BaseDamage);    // level / 3
        Assert.Equal(1, stats.MinDamage);
        Assert.Equal(4, stats.MaxDamage);
    }

    [Fact]
    public void Mob_defence_is_zero_unless_the_template_says_otherwise()
    {
        // It used to be level/4 on top of a defence that did not carry level at all. The formula
        // owns the level term now, so silence means "no harder than its level already makes it".
        var mob = NewMob(level: 40, stats: new() { { "health", 40 } });

        var defence = DamageCalculator.DefenderStatsFrom(mob);

        Assert.Equal(40, defence.Level);
        Assert.Equal(0, defence.DefenseRating);
        Assert.Equal(0, defence.Armor);
    }

    [Fact]
    public void Mob_damage_range_string_from_the_template_is_used()
    {
        var mob = NewMob(level: 9, stats: new() { { "damage", "4-7" } });

        var stats = DamageCalculator.StatsFrom(mob);

        Assert.Equal(4, stats.MinDamage);
        Assert.Equal(7, stats.MaxDamage);
        Assert.Equal(10, stats.AttackRating);
        Assert.Equal(3, stats.BaseDamage);
    }

    [Fact]
    public void Mob_damage_written_as_a_single_number_is_fixed_damage()
    {
        var mob = NewMob(level: 1, stats: new() { { "damage", 6 } });

        var stats = DamageCalculator.StatsFrom(mob);

        Assert.Equal(6, stats.MinDamage);
        Assert.Equal(6, stats.MaxDamage);
    }

    [Fact]
    public void Mob_explicit_damage_bounds_win_over_the_range_string()
    {
        var mob = NewMob(level: 1, stats: new()
        {
            { "damage", "4-7" },
            { "damageMax", 20 },
        });

        var stats = DamageCalculator.StatsFrom(mob);

        Assert.Equal(4, stats.MinDamage);
        Assert.Equal(20, stats.MaxDamage);
    }

    [Fact]
    public void Mob_damage_multiplier_scales_the_level_derived_baseline()
    {
        var mob = NewMob(level: 1, stats: new() { { "damageMultiplier", 3 } });

        var stats = DamageCalculator.StatsFrom(mob);

        Assert.Equal(3, stats.MinDamage);   // 1 x 3
        Assert.Equal(12, stats.MaxDamage);  // 4 x 3
    }

    [Fact]
    public void Mob_attack_rating_and_defence_come_from_the_template_when_declared()
    {
        var mob = NewMob(level: 4, stats: new()
        {
            { "attackRating", 15 },
            { "defense", 12 },
            { "armor", 30 },
        });

        var attack = DamageCalculator.StatsFrom(mob);
        var defence = DamageCalculator.DefenderStatsFrom(mob);

        Assert.Equal(15, attack.AttackRating);
        Assert.Equal(12, defence.DefenseRating);
        Assert.Equal(30, defence.Armor);
    }

    [Fact]
    public void Mob_damage_bounds_written_backwards_do_not_break_the_roll()
    {
        // random.Next(min, max + 1) throws when max < min, which would fault the combat tick
        // for every player fighting that mob rather than just looking wrong.
        var mob = NewMob(level: 1, stats: new() { { "damageMin", 9 }, { "damageMax", 2 } });

        var stats = DamageCalculator.StatsFrom(mob);
        var result = DamageCalculator.CalculateDamage(stats, Defender(), RollOf(15));

        Assert.True(stats.MaxDamage >= stats.MinDamage);
        Assert.True(result.DamageDealt >= 1);
    }

    private static Mob NewMob(int level, Dictionary<string, object> stats) => new()
    {
        TemplateKey = "kobold-sentry",
        RoomKey = "aldenmoor.millbrook.north-gate",
        Level = level,
        ResolvedStats = stats,
    };
}
