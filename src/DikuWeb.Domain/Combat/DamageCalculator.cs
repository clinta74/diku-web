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

        // On a crit the dice are rolled twice and summed, with the flat modifier added once -
        // the behaviour AttackResult documents. Taking the better of the two rolls instead, as
        // this did, moves the average so little that a natural 20 was indistinguishable from an
        // ordinary hit at low weapon dice.
        if (isCrit)
        {
            damageRolled += random.Next(attacker.MinDamage, attacker.MaxDamage + 1);
        }

        var totalDamage = damageRolled + attacker.BaseDamage;

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
    /// Build attacker stats from a mob, preferring what its template declares and falling back
    /// to level-derived defaults for anything it leaves out.
    /// </summary>
    /// <remarks>
    /// Every value here used to be level-derived unconditionally, so a template's own combat
    /// stats were carried all the way through spawning into <see cref="Mob.ResolvedStats"/> -
    /// multipliers and all - and then ignored.
    ///
    /// Damage dice may be written either as <c>damageMin</c>/<c>damageMax</c>, matching the
    /// vocabulary weapons use, or as the range string MobTemplate documents (<c>"damage": "4-7"</c>).
    /// A <c>damageMultiplier</c> scales whichever dice are in play, exactly as it does for a
    /// weapon, so the level-derived baseline is still worth scaling for a template that declares
    /// nothing else.
    /// </remarks>
    public static AttackerStats StatsFrom(Mob mob)
    {
        ArgumentNullException.ThrowIfNull(mob);

        var stats = mob.ResolvedStats;

        // Level-derived defaults, unchanged from before, used wherever the template is silent.
        var attackRating = StatReader.TryReadInt(stats, "attackRating", out var rating)
            ? rating
            : mob.Level / 2;

        var baseDamage = StatReader.TryReadInt(stats, "baseDamage", out var flat)
            ? flat
            : mob.Level / 3;

        var minDamage = 1;
        var maxDamage = 4;

        if (StatReader.TryReadRange(stats, "damage", out var rangeMin, out var rangeMax))
        {
            minDamage = rangeMin;
            maxDamage = rangeMax;
        }

        // Explicit bounds win over the range string, and either bound may be set alone.
        if (StatReader.TryReadInt(stats, "damageMin", out var declaredMin))
        {
            minDamage = declaredMin;
        }

        if (StatReader.TryReadInt(stats, "damageMax", out var declaredMax))
        {
            maxDamage = declaredMax;
        }

        if (StatReader.TryReadDecimal(stats, "damageMultiplier", out var multiplier) && multiplier > 0m)
        {
            // Ceiling, so a multiplier can never round a die face down to nothing.
            minDamage = (int)Math.Ceiling(minDamage * multiplier);
            maxDamage = (int)Math.Ceiling(maxDamage * multiplier);
        }

        // A template declaring max below min would otherwise make the roll throw.
        maxDamage = Math.Max(minDamage, maxDamage);

        return new AttackerStats(
            AttackRating: attackRating,
            BaseDamage: baseDamage,
            MinDamage: minDamage,
            MaxDamage: maxDamage);
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
    /// Build defender stats from a mob, preferring what its template declares and falling back
    /// to level-derived defence for anything it leaves out.
    /// </summary>
    public static DefenderStats DefenderStatsFrom(Mob mob)
    {
        ArgumentNullException.ThrowIfNull(mob);

        var stats = mob.ResolvedStats;

        var defense = StatReader.TryReadInt(stats, "defense", out var declared)
            ? declared
            : mob.Level / 4;

        StatReader.TryReadInt(stats, "armorFlat", out var armorFlat);
        StatReader.TryReadDecimal(stats, "armorPercent", out var armorPercent);

        if (StatReader.TryReadDecimal(stats, "armorMultiplier", out var multiplier) && multiplier > 0m)
        {
            armorFlat = (int)Math.Ceiling(armorFlat * multiplier);
        }

        return new DefenderStats(
            DefenseRating: defense,
            ArmorFlat: armorFlat,

            // Same 0-95% clamp equipment uses, so a mob cannot be authored immune.
            ArmorPercent: Math.Clamp(armorPercent, 0m, 0.95m));
    }
}
