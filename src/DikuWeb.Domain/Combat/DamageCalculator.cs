using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Randomness;

namespace DikuWeb.Domain.Combat;

/// <summary>
/// Input to damage calculation: the attacker's resolved combat stats.
/// </summary>
public sealed record AttackerStats(
    int AttackRating,
    int BaseDamage,
    int MinDamage,
    int MaxDamage);

/// <summary>
/// Input to damage calculation: the defender's resolved combat stats.
/// </summary>
public sealed record DefenderStats(
    int DefenseRating,
    int ArmorFlat,
    decimal ArmorPercent);

/// <summary>
/// Outcome of a single attack: hit/miss/crit, and if hit, damage dealt.
/// </summary>
public sealed record DamageResult(
    bool Hit,
    bool IsCritical,
    int DamageDealt,
    int NaturalRoll)
{
    public DamageResult(int naturalRoll, bool hit, bool isCrit, int damage)
        : this(hit, isCrit, damage, naturalRoll) { }
}

/// <summary>
/// Pure damage calculation per PLAN.md §4.6.
///
/// Combat formula:
///   attackRoll  = d20 + attackRating
///   defenseVal  = 10 + defenseRating
///   miss   if attackRoll &lt; defenseVal
///   hit    if attackRoll ≥ defenseVal
///   crit   if natural 20, or beats defenseVal by 10+
///
///   damage = roll weapon dice + MightMod (passed as baseDamage)
///   final  = max(1, (damage − armorFlat) × (1 − armorPercent))
///
/// No world state, no side effects. Given the same inputs and random seed,
/// always returns the same result.
/// </summary>
public static class DamageCalculator
{
    /// <summary>
    /// Calculate damage for one attack.
    /// </summary>
    public static DamageResult CalculateDamage(
        AttackerStats attacker,
        DefenderStats defender,
        IRandomSource random)
    {
        var naturalRoll = random.Next(1, 21); // d20: 1-20
        var attackRoll = naturalRoll + attacker.AttackRating;
        var defenseVal = 10 + defender.DefenseRating;

        // Check for hit/miss
        var isHit = attackRoll >= defenseVal;
        if (!isHit)
        {
            return new DamageResult(naturalRoll, false, false, 0);
        }

        // Check for crit: natural 20 or beats defense by 10+
        var isCrit = naturalRoll == 20 || (attackRoll - defenseVal) >= 10;

        // Calculate base damage
        var damageRolled = random.Next(attacker.MinDamage, attacker.MaxDamage + 1);
        var totalDamage = damageRolled + attacker.BaseDamage;

        // If crit, roll damage twice and take the better
        if (isCrit)
        {
            var critDamage = random.Next(attacker.MinDamage, attacker.MaxDamage + 1);
            var critTotal = critDamage + attacker.BaseDamage;
            totalDamage = Math.Max(totalDamage, critTotal);
        }

        // Apply armor reduction: (damage - flatReduction) * (1 - percentReduction)
        var afterFlat = totalDamage - defender.ArmorFlat;
        var afterPercent = (decimal)afterFlat * (1m - defender.ArmorPercent);
        var final = (int)afterPercent;

        // Never drop below 1 damage on a hit
        final = Math.Max(1, final);

        return new DamageResult(naturalRoll, true, isCrit, final);
    }

    /// <summary>
    /// Build attacker stats from a player character.
    /// Unarmed damage: 1d4 + might modifier.
    /// </summary>
    public static AttackerStats StatsFrom(Character character)
    {
        var attackRating = (character.Level / 2) + character.Attributes.MightModifier;
        var mightMod = character.Attributes.MightModifier;
        return new AttackerStats(
            AttackRating: attackRating,
            BaseDamage: mightMod,
            MinDamage: 1,
            MaxDamage: 4);
    }

    /// <summary>
    /// Build attacker stats from a mob.
    /// Mobs use level-based attack rating and damage (no attributes).
    /// </summary>
    public static AttackerStats StatsFrom(Mob mob)
    {
        var attackRating = mob.Level / 2;
        return new AttackerStats(
            AttackRating: attackRating,
            BaseDamage: mob.Level / 3, // Level-scaled damage
            MinDamage: 1,
            MaxDamage: 4);
    }

    /// <summary>
    /// Build defender stats from a player character.
    /// No armor yet; defense is agility modifier + base 10.
    /// </summary>
    public static DefenderStats DefenderStatsFrom(Character character)
    {
        return new DefenderStats(
            DefenseRating: character.Attributes.AgilityModifier,
            ArmorFlat: 0,
            ArmorPercent: 0m);
    }

    /// <summary>
    /// Build defender stats from a mob.
    /// Mobs have flat defense scaling with level.
    /// </summary>
    public static DefenderStats DefenderStatsFrom(Mob mob)
    {
        return new DefenderStats(
            DefenseRating: mob.Level / 4,
            ArmorFlat: 0,
            ArmorPercent: 0m);
    }
}
