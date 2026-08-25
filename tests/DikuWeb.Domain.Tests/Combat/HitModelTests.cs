using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Randomness;

namespace DikuWeb.Domain.Tests.Combat;

/// <summary>
/// The design rule the hit model exists to serve, asserted directly rather than inferred from
/// tuning numbers: <b>anything that still pays experience must be able to hurt you without needing
/// a critical.</b>
/// </summary>
/// <remarks>
/// <para>
/// This is the test the old d20 could not have passed, and the reason it was replaced. The window
/// of mobs that still pay is <c>[XpRelevance.Floor(L), L]</c> — half your level and up — so it
/// widens as you climb, while a d20 has nineteen usable faces forever. At level 48 the level gap
/// alone wanted twelve of them, and the bottom third of the paying window fell onto the clamp,
/// where the only face that lands is also the critical. A level 28 mob worth a fifth of full
/// experience could hit a level 48 <em>only</em> for doubled damage.
/// </para>
/// <para>
/// Written against <see cref="XpRelevance"/> rather than against a level difference, so the two
/// stay married: widen the experience window and this fails until the combat model can carry it.
/// That coupling is the point — reward and risk are supposed to be the same question.
/// </para>
/// </remarks>
public class HitModelTests
{
    /// <summary>
    /// The least a mob may be able to land and still be said to carry risk. Deliberately loose:
    /// this is catching a wall, not tuning difficulty.
    /// </summary>
    private const double LeastMeaningfulRisk = 0.15;

    /// <summary>
    /// A character at this level in ordinary gear for it: the Agility modifier caps at +5 and a
    /// shield at +5, and neither is available at level 5. Not a best-in-slot fantasy - the question
    /// is whether the floor of the paying window can still reach somebody sensibly equipped.
    /// </summary>
    private static DefenderStats Player(int level, int guard = 0) =>
        new(level, DefenseRating: OrdinaryGear(level) + guard, Armor: (int)(3.5 * level));

    /// <summary>Agility and a shield, both of which arrive over the first third of the game.</summary>
    private static int OrdinaryGear(int level) => Math.Min(9, 1 + (level / 3));

    /// <summary>A mob of this level carrying nothing its template did not give it.</summary>
    private static AttackerStats Mob(int level) =>
        new(
            Level: level,
            AttackRating: (int)Math.Round(DamageCalculator.MobSkill * level, MidpointRounding.AwayFromZero),
            BaseDamage: 0,
            MinDamage: 1 + (level / 2),
            MaxDamage: 4 + (3 * level / 2));

    [Fact]
    public void Everything_that_still_pays_experience_can_still_hurt_you()
    {
        var failures = new List<string>();

        for (var level = 5; level <= XpProgression.MaxLevel; level++)
        {
            for (var mobLevel = XpRelevance.Floor(level); mobLevel <= level; mobLevel++)
            {
                var chance = DamageCalculator.HitChance(Mob(mobLevel), Player(level));

                if (chance < LeastMeaningfulRisk)
                {
                    failures.Add(
                        $"level {level} vs mob {mobLevel} " +
                        $"(worth {XpRelevance.Fraction(level, mobLevel):P0} of full xp): {chance:P1} to land");
                }
            }
        }

        Assert.True(
            failures.Count == 0,
            "Paying experience without carrying risk:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void And_still_can_with_the_strongest_guard_in_the_game_running()
    {
        // The Last Wall is +18 defenceRating, the largest a character can put on themselves, and it
        // was the ability that produced the original bug report. A defensive cooldown is meant to
        // make a fight survivable, not to switch the fight off.
        //
        // The bar is clear of the floor rather than generous: with the best guard in the game up,
        // against the very weakest thing that still pays, half again the clamp is the whole ask.
        // What matters is that the clamp is not the thing deciding and that the blows which land
        // are ordinary - No_mob_in_the_paying_window_lands_only_criticals covers the second half.
        var failures = new List<string>();

        for (var level = 20; level <= XpProgression.MaxLevel; level++)
        {
            for (var mobLevel = XpRelevance.Floor(level); mobLevel <= level; mobLevel++)
            {
                var chance = DamageCalculator.HitChance(Mob(mobLevel), Player(level, guard: 18));

                if (chance < DamageCalculator.MinHitChance * 1.5)
                {
                    failures.Add($"level {level} vs mob {mobLevel}: {chance:P1} to land");
                }
            }
        }

        Assert.True(
            failures.Count == 0,
            "A guard switched the fight off:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// No blow that lands may be a critical more often than the critical rate, anywhere in the
    /// paying window. This is the reported symptom stated as an invariant.
    /// </summary>
    [Fact]
    public void No_mob_in_the_paying_window_lands_only_criticals()
    {
        foreach (var level in new[] { 10, 25, 40, 48 })
        {
            foreach (var mobLevel in new[] { XpRelevance.Floor(level), (XpRelevance.Floor(level) + level) / 2, level })
            {
                var attacker = Mob(mobLevel);
                var defender = Player(level, guard: 18);
                var random = new SeededRandomSource(20260825);

                int hits = 0, crits = 0;
                for (var i = 0; i < 100_000; i++)
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

                Assert.True(hits > 0, $"level {level} vs mob {mobLevel} never landed anything");

                var share = (double)crits / hits;
                Assert.True(
                    share < 0.10,
                    $"level {level} vs mob {mobLevel}: {share:P0} of landed blows were critical");
            }
        }
    }

    /// <summary>
    /// The window has to behave the same way at every level, or the game breaks by being played -
    /// which is exactly how the previous model failed.
    /// </summary>
    [Fact]
    public void The_floor_of_the_paying_window_is_the_same_fight_at_every_level()
    {
        double? expected = null;

        foreach (var level in new[] { 10, 20, 30, 40, 50 })
        {
            var chance = DamageCalculator.HitChance(Mob(XpRelevance.Floor(level)), Player(level));

            expected ??= chance;
            Assert.InRange(chance, expected.Value - 0.05, expected.Value + 0.05);
        }
    }

    [Fact]
    public void A_level_appropriate_fight_is_close_to_even()
    {
        foreach (var level in new[] { 10, 20, 30, 40, 50 })
        {
            var chance = DamageCalculator.HitChance(Mob(level), Player(level));

            Assert.InRange(chance, 0.35, 0.60);
        }
    }

    [Fact]
    public void Gear_and_guards_reduce_risk_without_ever_removing_it()
    {
        const int Level = 40;
        var mob = Mob(Level);

        var bare = DamageCalculator.HitChance(mob, new DefenderStats(Level, 0, 0));
        var geared = DamageCalculator.HitChance(mob, Player(Level));
        var guarded = DamageCalculator.HitChance(mob, Player(Level, guard: 18));

        Assert.True(geared < bare, "gear has to be worth something");
        Assert.True(guarded < geared, "a guard has to be worth something on top of gear");
        Assert.True(guarded > LeastMeaningfulRisk, "and none of it may switch the fight off");
    }
}
