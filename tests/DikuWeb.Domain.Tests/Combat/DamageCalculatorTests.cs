using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Randomness;
using DikuWeb.Domain.Tests.Infrastructure;

namespace DikuWeb.Domain.Tests.Combat;

public class DamageCalculatorTests
{
    private static AttackerStats Attacker(
        int level = 10,
        int attackRating = 100,
        int baseDamage = 0,
        int minDamage = 1,
        int maxDamage = 6) =>
        new(level, attackRating, baseDamage, minDamage, maxDamage);

    private static DefenderStats Defender(
        int level = 10,
        int defenseRating = 0,
        int armor = 0,
        decimal mitigationDelta = 0m) =>
        new(level, defenseRating, armor, mitigationDelta);

    /// <summary>How often this pairing landed, over enough swings for a percent to mean something.</summary>
    private static double MeasuredHitRate(AttackerStats attacker, DefenderStats defender, int samples = 40_000)
    {
        var random = new SeededRandomSource(20260825);
        var hits = 0;

        for (var i = 0; i < samples; i++)
        {
            if (DamageCalculator.CalculateDamage(attacker, defender, random).Hit)
            {
                hits++;
            }
        }

        return (double)hits / samples;
    }

    // =========================================================================
    // Hit chance is a ratio of powers
    // =========================================================================

    [Fact]
    public void An_even_match_is_a_coin()
    {
        // Equal accuracy against equal evasion is 50/50 by construction - the ratio's whole point
        // is that this is true of the arithmetic rather than of a chosen constant.
        Assert.Equal(0.5, DamageCalculator.HitChance(Attacker(attackRating: 40), Defender(level: 40)), precision: 6);
    }

    [Fact]
    public void What_a_swing_rolls_against_is_what_it_actually_lands()
    {
        // The measured rate has to match the advertised one, or the number the stats screen shows
        // is decoration.
        var attacker = Attacker(attackRating: 60);
        var defender = Defender(level: 40, defenseRating: 10);

        var advertised = DamageCalculator.HitChance(attacker, defender);

        Assert.InRange(MeasuredHitRate(attacker, defender), advertised - 0.01, advertised + 0.01);
    }

    [Fact]
    public void More_accuracy_helps_and_more_evasion_hurts()
    {
        var baseline = DamageCalculator.HitChance(Attacker(attackRating: 50), Defender(level: 40));

        Assert.True(DamageCalculator.HitChance(Attacker(attackRating: 70), Defender(level: 40)) > baseline);
        Assert.True(DamageCalculator.HitChance(Attacker(attackRating: 50), Defender(level: 40, defenseRating: 20)) < baseline);
    }

    [Fact]
    public void A_higher_level_defender_is_harder_to_hit()
    {
        var attacker = Attacker(attackRating: 50);

        Assert.True(
            DamageCalculator.HitChance(attacker, Defender(level: 40)) <
            DamageCalculator.HitChance(attacker, Defender(level: 10)));
    }

    /// <summary>
    /// The property the d20 could not have, and the reason it was replaced.
    /// </summary>
    /// <remarks>
    /// The old formula spent <c>(defLevel − attLevel)/2</c> faces on the level gap out of nineteen,
    /// while the gap that still pays experience is <c>L/2</c> levels wide — so the same matchup got
    /// steadily more lopsided the higher both sides were. A ratio has no width to run out of: an
    /// attacker at three quarters of the defender's level faces the same odds at every level.
    /// </remarks>
    [Fact]
    public void The_same_matchup_is_the_same_odds_at_every_level()
    {
        double? expected = null;

        foreach (var level in new[] { 8, 16, 24, 32, 40, 48 })
        {
            var attacker = Attacker(level: level * 3 / 4, attackRating: level * 3 / 4);
            var chance = DamageCalculator.HitChance(attacker, Defender(level: level));

            expected ??= chance;
            Assert.InRange(chance, expected.Value - 0.02, expected.Value + 0.02);
        }
    }

    // =========================================================================
    // Nobody is ever certain, either way
    // =========================================================================

    [Fact]
    public void The_most_outmatched_attacker_still_lands_something()
    {
        var hopeless = Attacker(attackRating: 1);
        var fortress = Defender(level: 50, defenseRating: 100_000, armor: 100_000);

        Assert.Equal(DamageCalculator.MinHitChance, DamageCalculator.HitChance(hopeless, fortress));
        Assert.InRange(MeasuredHitRate(hopeless, fortress), 0.03, 0.07);
    }

    [Fact]
    public void The_most_overwhelming_attacker_still_misses_sometimes()
    {
        var overwhelming = Attacker(attackRating: 100_000);
        var helpless = Defender(level: 1, defenseRating: -50);

        Assert.Equal(DamageCalculator.MaxHitChance, DamageCalculator.HitChance(overwhelming, helpless));
        Assert.InRange(MeasuredHitRate(overwhelming, helpless), 0.93, 0.97);
    }

    [Fact]
    public void Being_defenceless_is_never_a_defence()
    {
        // Evasion is floored before it is squared. A negative agility modifier, or an expose that
        // strips a guard past nothing, would otherwise square back into a positive and protect.
        var attacker = Attacker(attackRating: 30);

        Assert.True(
            DamageCalculator.HitChance(attacker, Defender(level: 0, defenseRating: -40)) >=
            DamageCalculator.HitChance(attacker, Defender(level: 0, defenseRating: 0)));
    }

    [Fact]
    public void An_attacker_with_no_accuracy_at_all_is_not_a_divide_by_zero()
    {
        Assert.Equal(DamageCalculator.MinHitChance, DamageCalculator.HitChance(Attacker(attackRating: 0), Defender()));
        Assert.Equal(DamageCalculator.MinHitChance, DamageCalculator.HitChance(Attacker(attackRating: -5), Defender()));
    }

    // =========================================================================
    // Criticals are their own roll
    // =========================================================================

    /// <summary>
    /// The bug that ended the d20, asserted so it cannot come back.
    /// </summary>
    /// <remarks>
    /// A critical used to be a natural 20, which is also the only face that beats a maxed-out
    /// defence — so the harder a defender was to hit, the larger the share of what reached them was
    /// doubled, reaching *all of it* at the clamp. A level 48 saw a level 28 mob, still worth a
    /// fifth of full experience, land nothing but criticals.
    /// </remarks>
    [Fact]
    public void The_critical_share_of_landed_blows_does_not_move_with_the_defence()
    {
        foreach (var defenceRating in new[] { 0, 10, 40, 200, 100_000 })
        {
            var attacker = Attacker(attackRating: 50, minDamage: 1, maxDamage: 6);
            var defender = Defender(level: 30, defenseRating: defenceRating);
            var random = new SeededRandomSource(4242);

            int hits = 0, crits = 0;
            for (var i = 0; i < 200_000; i++)
            {
                var result = DamageCalculator.CalculateDamage(attacker, defender, random);
                if (!result.Hit)
                {
                    continue;
                }

                hits++;
                if (result.IsCritical)
                {
                    crits++;
                }
            }

            var share = (double)crits / hits;
            Assert.InRange(share, DamageCalculator.CriticalChance - 0.015, DamageCalculator.CriticalChance + 0.015);
        }
    }

    [Fact]
    public void A_missed_swing_is_never_a_critical()
    {
        var result = DamageCalculator.CalculateDamage(Attacker(), Defender(), FixedChanceSource.Never);

        Assert.False(result.Hit);
        Assert.False(result.IsCritical);
        Assert.Equal(0, result.DamageDealt);
    }

    [Fact]
    public void Critical_hits_sum_both_dice_rather_than_taking_the_better()
    {
        // Fixed dice make the rule visible: with a 4-4 weapon and no modifier, a crit must deal 8.
        var attacker = Attacker(attackRating: 100, baseDamage: 0, minDamage: 4, maxDamage: 4);

        var result = DamageCalculator.CalculateDamage(attacker, Defender(), FixedChanceSource.Always);

        Assert.True(result.IsCritical);
        Assert.Equal(8, result.DamageDealt);
    }

    [Fact]
    public void The_flat_modifier_is_added_once_on_a_crit_not_twice()
    {
        // Dice twice, modifier once: a 4-4 weapon with +3 Might crits for 4 + 4 + 3.
        var attacker = Attacker(attackRating: 100, baseDamage: 3, minDamage: 4, maxDamage: 4);

        var result = DamageCalculator.CalculateDamage(attacker, Defender(), FixedChanceSource.Always);

        Assert.True(result.IsCritical);
        Assert.Equal(11, result.DamageDealt);
    }

    [Fact]
    public void An_ordinary_hit_rolls_the_dice_once()
    {
        var attacker = Attacker(attackRating: 100, baseDamage: 3, minDamage: 4, maxDamage: 4);

        var result = DamageCalculator.CalculateDamage(attacker, Defender(), FixedChanceSource.OrdinaryHit());

        Assert.True(result.Hit);
        Assert.False(result.IsCritical);
        Assert.Equal(7, result.DamageDealt);
    }

    // =========================================================================
    // Armour absorbs a fraction, measured against the attacker
    // =========================================================================

    [Fact]
    public void Armor_reduces_damage()
    {
        var attacker = Attacker(level: 10, attackRating: 100, baseDamage: 10, minDamage: 10, maxDamage: 10);

        var bare = DamageCalculator.CalculateDamage(attacker, Defender(), FixedChanceSource.OrdinaryHit());
        var armoured = DamageCalculator.CalculateDamage(attacker, Defender(armor: 100), FixedChanceSource.OrdinaryHit());

        Assert.True(armoured.DamageDealt < bare.DamageDealt);
    }

    [Fact]
    public void Armor_matching_the_attackers_bite_halves_the_blow()
    {
        // The one sentence every authored armour value is chosen against: a rating of
        // Bite x attackerLevel absorbs half.
        var attacker = Attacker(level: 10, attackRating: 100, baseDamage: 0, minDamage: 20, maxDamage: 20);
        var defender = Defender(armor: ArmorCurve.Bite * 10);

        var result = DamageCalculator.CalculateDamage(attacker, defender, FixedChanceSource.OrdinaryHit());

        Assert.Equal(10, result.DamageDealt);
    }

    /// <summary>
    /// The property a global constant could not give armour, and the reason it was replaced.
    /// </summary>
    /// <remarks>
    /// <c>armor / (armor + 100)</c> meant an armour point was worth the same against everything, so
    /// mitigation crept upward tier by tier - 20% in Ossara to 60% in Nemhal, measured across the
    /// authored realms - and endgame content was on its way to simply sitting on the cap.
    /// </remarks>
    [Fact]
    public void The_same_armour_absorbs_more_from_a_weaker_attacker()
    {
        Assert.True(ArmorCurve.Mitigation(150, attackerLevel: 20) > ArmorCurve.Mitigation(150, attackerLevel: 40));
    }

    [Fact]
    public void A_level_appropriate_set_absorbs_the_same_share_at_every_tier()
    {
        // Content authors a full set at roughly 3.5 x level. That has to mean the same thing in
        // Ossara and in the Unlit, or builders are aiming at a number they cannot see.
        decimal? expected = null;

        foreach (var level in new[] { 6, 18, 29, 40, 50 })
        {
            var absorbed = ArmorCurve.Mitigation((int)(3.5 * level), attackerLevel: level);

            expected ??= absorbed;
            Assert.InRange(absorbed, expected.Value - 0.02m, expected.Value + 0.02m);
        }
    }

    [Fact]
    public void No_amount_of_armor_reaches_immunity()
    {
        var attacker = Attacker(level: 50, attackRating: 100, baseDamage: 0, minDamage: 1000, maxDamage: 1000);
        var defender = Defender(armor: int.MaxValue);

        var result = DamageCalculator.CalculateDamage(attacker, defender, FixedChanceSource.OrdinaryHit());

        // The cap leaves a quarter of every blow that lands.
        Assert.Equal(250, result.DamageDealt);
    }

    [Fact]
    public void Armor_cannot_reduce_a_landed_blow_below_one()
    {
        var attacker = Attacker(level: 50, attackRating: 100, baseDamage: 0, minDamage: 1, maxDamage: 1);

        var result = DamageCalculator.CalculateDamage(attacker, Defender(armor: 100_000), FixedChanceSource.OrdinaryHit());

        Assert.True(result.Hit);
        Assert.Equal(1, result.DamageDealt);
    }

    [Fact]
    public void Negative_armor_absorbs_nothing_rather_than_amplifying()
    {
        var attacker = Attacker(level: 10, attackRating: 100, baseDamage: 0, minDamage: 10, maxDamage: 10);

        var result = DamageCalculator.CalculateDamage(attacker, Defender(armor: -500), FixedChanceSource.OrdinaryHit());

        Assert.Equal(10, result.DamageDealt);
    }

    [Fact]
    public void A_guard_effects_mitigation_rides_beside_the_rating()
    {
        // Carried on DefenderStats rather than folded back into an armour rating: the curve reads
        // the attacker's level now, so there is no rating that means "this much absorption" until
        // you know who is swinging.
        var attacker = Attacker(level: 10, attackRating: 100, baseDamage: 0, minDamage: 100, maxDamage: 100);

        var plain = DamageCalculator.CalculateDamage(attacker, Defender(armor: 50), FixedChanceSource.OrdinaryHit());
        var guarded = DamageCalculator.CalculateDamage(
            attacker, Defender(armor: 50, mitigationDelta: 0.20m), FixedChanceSource.OrdinaryHit());

        Assert.Equal(plain.DamageDealt - 20, guarded.DamageDealt);
    }

    [Fact]
    public void Stacked_mitigation_is_still_held_at_the_cap()
    {
        var attacker = Attacker(level: 10, attackRating: 100, baseDamage: 0, minDamage: 100, maxDamage: 100);
        var defender = Defender(armor: 50, mitigationDelta: 0.90m);

        var result = DamageCalculator.CalculateDamage(attacker, defender, FixedChanceSource.OrdinaryHit());

        Assert.Equal(25, result.DamageDealt);
    }

    // =========================================================================
    // Determinism and reporting
    // =========================================================================

    [Fact]
    public void Same_inputs_same_seed_same_output()
    {
        var attacker = Attacker(level: 12, attackRating: 40, baseDamage: 3, minDamage: 2, maxDamage: 8);
        var defender = Defender(level: 12, defenseRating: 4, armor: 30);

        var first = DamageCalculator.CalculateDamage(attacker, defender, new SeededRandomSource(42));
        var second = DamageCalculator.CalculateDamage(attacker, defender, new SeededRandomSource(42));

        Assert.Equal(first, second);
    }

    [Fact]
    public void The_result_reports_the_chance_it_was_measured_against()
    {
        var attacker = Attacker(attackRating: 55);
        var defender = Defender(level: 30, defenseRating: 5);

        var result = DamageCalculator.CalculateDamage(attacker, defender, new SeededRandomSource(1));

        Assert.Equal(DamageCalculator.HitChance(attacker, defender), result.HitChance, precision: 10);
    }

    [Fact]
    public void Damage_varies_within_weapon_range()
    {
        var attacker = Attacker(attackRating: 100, baseDamage: 0, minDamage: 1, maxDamage: 6);
        var dealt = new HashSet<int>();

        for (var seed = 0; seed < 200; seed++)
        {
            var result = DamageCalculator.CalculateDamage(attacker, Defender(), FixedChanceSource.OrdinaryHit(seed));
            if (result.Hit)
            {
                dealt.Add(result.DamageDealt);
            }
        }

        Assert.True(dealt.Count > 1, "Damage should vary across runs");
        Assert.All(dealt, d => Assert.InRange(d, 1, 6));
    }

    // =========================================================================
    // Edge Cases
    // =========================================================================

    [Fact]
    public void Zero_base_damage_still_hits()
    {
        var attacker = Attacker(attackRating: 100, baseDamage: 0, minDamage: 1, maxDamage: 6);

        var result = DamageCalculator.CalculateDamage(attacker, Defender(), FixedChanceSource.OrdinaryHit());

        Assert.True(result.Hit);
        Assert.True(result.DamageDealt >= 1);
    }

    [Fact]
    public void Negative_base_damage_reduced_by_roll()
    {
        // Fixed 1-1 dice so the clamp is what is being tested: a hit gives 1 - 2 = -1, which floors.
        var attacker = Attacker(attackRating: 100, baseDamage: -2, minDamage: 1, maxDamage: 1);

        var result = DamageCalculator.CalculateDamage(attacker, Defender(), FixedChanceSource.OrdinaryHit());

        Assert.True(result.Hit);
        Assert.Equal(1, result.DamageDealt);
    }

    [Fact]
    public void Very_high_damage_values_work()
    {
        var attacker = Attacker(level: 10, attackRating: 100, baseDamage: 1000, minDamage: 100, maxDamage: 200);

        var result = DamageCalculator.CalculateDamage(attacker, Defender(armor: 10), FixedChanceSource.OrdinaryHit());

        Assert.True(result.Hit);

        // 1100 at the very least, less the ~17% a rating of 10 absorbs from a level 10 attacker.
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

        // The mob's level, times the skill factor that stands for competence rather than level.
        // This was `level/2 + 6` while the number had to fit inside twenty faces beside a base
        // of 10; accuracy is one side of a ratio now and has no budget to fit inside.
        Assert.Equal(9, stats.Level);
        Assert.Equal(11, stats.AttackRating);   // round(1.25 x 9)

        // Every face of the die scales, and the flat adder is gone: all the level scaling is in
        // one place, so an authored `damage` means what it says.
        Assert.Equal(0, stats.BaseDamage);
        Assert.Equal(5, stats.MinDamage);     // 1 + level/2
        Assert.Equal(17, stats.MaxDamage);    // 4 + 3*level/2
    }

    [Fact]
    public void Mob_damage_dice_keep_their_spread_as_they_scale()
    {
        // The failure the flat adder produced was not only that it fell behind. A level 50 mob
        // dealt 17-20 - an eight percent spread, so no exchange was luckier than any other and
        // the dice had quietly stopped being rolled. The ratio has to survive the scaling.
        foreach (var level in new[] { 1, 10, 25, 50 })
        {
            var stats = DamageCalculator.StatsFrom(NewMob(level, stats: new() { { "health", 40 } }));
            var spread = (double)stats.MaxDamage / stats.MinDamage;

            Assert.True(spread >= 2.5, $"level {level} deals {stats.MinDamage}-{stats.MaxDamage}, spread {spread:F1}");
        }
    }

    [Fact]
    public void Mob_damage_grows_faster_than_the_health_it_is_thrown_at()
    {
        // Player health grows by 5 a level. Damage that grew by a third of a level meant fights
        // got steadily *longer* as the game went on - about thirty landed blows to kill a player
        // at level 1 and fifty-three at level 50, before mitigation, which then widened it.
        var low = DamageCalculator.StatsFrom(NewMob(level: 5, stats: new() { { "health", 40 } }));
        var high = DamageCalculator.StatsFrom(NewMob(level: 45, stats: new() { { "health", 40 } }));

        var lowAverage = (low.MinDamage + low.MaxDamage) / 2.0;
        var highAverage = (high.MinDamage + high.MaxDamage) / 2.0;

        // Health over the same span goes from 88 to 290, a little over three times. Damage has to
        // clearly outpace that, because mitigation rises with the tiers on top of it.
        Assert.True(
            highAverage / lowAverage >= 6,
            $"level 5 averages {lowAverage}, level 45 averages {highAverage}");
    }

    [Fact]
    public void Mob_defence_is_zero_unless_the_template_says_otherwise()
    {
        // The mob's own level is already the bulk of its evasion, so silence means "no harder to
        // hit than its level already makes it".
        var mob = NewMob(level: 40, stats: new() { { "health", 40 } });

        var defence = DamageCalculator.DefenderStatsFrom(mob);

        Assert.Equal(40, defence.Level);
        Assert.Equal(0, defence.DefenseRating);
        Assert.Equal(0, defence.Armor);
        Assert.Equal(0m, defence.MitigationDelta);
    }

    [Fact]
    public void Mob_damage_range_string_from_the_template_is_used()
    {
        var mob = NewMob(level: 9, stats: new() { { "damage", "4-7" } });

        var stats = DamageCalculator.StatsFrom(mob);

        Assert.Equal(4, stats.MinDamage);
        Assert.Equal(7, stats.MaxDamage);
        Assert.Equal(11, stats.AttackRating);

        // No hidden adder on top of authored dice. A template that says 4-7 deals 4-7, and its
        // level scaling comes from the zone dials (§4.4) like everything else about it.
        Assert.Equal(0, stats.BaseDamage);
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

        Assert.Equal(3, stats.MinDamage);   // (1 + 0) x 3
        Assert.Equal(15, stats.MaxDamage);  // (4 + 1) x 3
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

        // An authored rating replaces the level, and then takes the same skill factor every other
        // mob takes - so two mobs with the same number are equally accurate however they got it.
        Assert.Equal(19, attack.AttackRating);   // round(1.25 x 15)
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

        // Forced to land, because what is being tested is the roll not throwing - a level 1 mob
        // against anything else would spend the test missing.
        var result = DamageCalculator.CalculateDamage(stats, Defender(level: 1), FixedChanceSource.Always);

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
