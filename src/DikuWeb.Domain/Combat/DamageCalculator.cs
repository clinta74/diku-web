using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Randomness;

namespace DikuWeb.Domain.Combat;

/// <summary>
/// Input to damage calculation: the attacker's resolved combat stats.
/// </summary>
/// <param name="Level">
/// The level the attacker fights at. Read by <see cref="ArmorCurve"/>, which measures a defender's
/// armour against the attacker rather than against a constant.
/// </param>
/// <param name="AttackRating">
/// Accuracy: the attacker's level, attribute and gear, already multiplied by the skill factor for
/// what kind of combatant this is. See <see cref="DamageCalculator.CharacterSkill"/>.
/// </param>
public sealed record AttackerStats(
    int Level,
    int AttackRating,
    int BaseDamage,
    int MinDamage,
    int MaxDamage);

/// <summary>
/// Input to damage calculation: the defender's resolved combat stats.
/// </summary>
/// <param name="Level">
/// The level the defender fights at, and the base of their evasion — see
/// <see cref="DamageCalculator.Evasion"/>.
/// </param>
/// <param name="DefenseRating">
/// Everything beyond the defender's own level that makes them hard to hit: the Agility modifier,
/// item <c>defense</c>, and any guard effect running. Raises evasion by a share of the bearer's
/// level rather than by a flat amount (<see cref="DamageCalculator.GearScale"/>), so an authored
/// number means the same thing at every tier.
/// </param>
/// <param name="Armor">
/// Summed armor rating, converted to a fraction by <see cref="ArmorCurve"/>. Replaced the old
/// <c>ArmorFlat</c>/<c>ArmorPercent</c> pair: one number the author sets, one curve that reads it.
/// </param>
/// <param name="MitigationDelta">
/// Percentage points of extra absorption from active effects, as a fraction. Carried here rather
/// than folded back into <paramref name="Armor"/>: armour is now read against the attacker's level,
/// so there is no longer a single rating that means "this much absorption" for every attacker, and
/// the inverse the engine used to compute had nothing left to invert.
/// </param>
public sealed record DefenderStats(
    int Level,
    int DefenseRating,
    int Armor,
    decimal MitigationDelta = 0m);

/// <summary>
/// Outcome of a single attack: hit/miss/crit, and if hit, damage dealt.
/// </summary>
/// <param name="HitChance">
/// The probability this swing was measured against. Kept on the result because it is the one number
/// that explains an exchange after the fact, and the balance harness reads it directly rather than
/// inferring it from thousands of samples.
/// </param>
public sealed record DamageResult(
    bool Hit,
    bool IsCritical,
    int DamageDealt,
    double HitChance);

/// <summary>
/// Pure damage calculation per PLAN.md §4.6.
///
/// Combat formula:
///   accuracy   = skill × level × (1 + (attackAttrMod + weaponBonus)/30)  [in AttackRating]
///   evasion    = level × (1 + (agilityMod + Σ item defense + Σ guards)/30)
///   hitChance  = clamp(accuracy² / (accuracy² + evasion²), 0.05, 0.95)
///   crit       = an independent roll at CriticalChance, on a blow that landed
///
///   damage     = roll weapon dice + MightMod (passed as baseDamage)
///   final      = max(1, damage × (1 − ArmorCurve.Mitigation(armor, attacker level)))
///
/// No world state, no side effects. Given the same inputs and random seed,
/// always returns the same result.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ratios, not a die, because the reward window widens with level and a die does not.</b> This
/// was a d20: <c>needed = clamp(10 + defLevel/2 + defenseRating − attackRating, 2, 20)</c>. Both
/// sides carried <c>level/2</c> so it cancelled, which left an evenly matched exchange reducing to
/// <c>needed = 4 + defenseRating</c> — sixteen faces for gear, attributes and every buff at once.
/// Worse, the level gap entered as a <em>difference</em>, costing <c>(defLevel − attLevel)/2</c>
/// faces, while the window of mobs that still pay experience is <c>[L/2, L]</c>
/// (<c>XpRelevance.Floor</c>) and therefore <c>L/2</c> levels wide. At level 48 that gap alone
/// wanted twelve of the nineteen usable faces. The die could express the fight at level 10 and not
/// at level 48, so the system broke by being played.
/// </para>
/// <para>
/// A ratio has no width to run out of. <c>A² / (A² + D²)</c> is scale-free by construction: the
/// experience window maps to the same band of hit chances at level 10 and at level 50, and evasion
/// sits in a denominator, so no amount of it can produce a defender nothing can reach. That is why
/// there is no defence budget here any more, and nothing that needs a saturating curve to stay
/// inside one.
/// </para>
/// <para>
/// <b>Squared, and the exponent is borrowed rather than invented.</b> <see cref="MobLevel"/> already
/// anchors the game to power ∝ level², derived from the experience curve <c>1000·L·(L−1)/2</c>. So
/// <c>A²/(A²+D²)</c> is the ratio of combat <em>powers</em>, and one idea of what a level is worth
/// serves both progression and the swing. A linear ratio leaves the whole window inside fourteen
/// points, so out-levelling stops meaning anything; a cube compresses the bottom of the window hard
/// enough to bring back the problem this replaced.
/// </para>
/// <para>
/// <b>The clamps are the guarantee, not the tuning.</b> <see cref="MinHitChance"/> and
/// <see cref="MaxHitChance"/> mean nobody is ever certain either way, whatever anyone authors —
/// the same promise the old natural 1 and natural 20 made, without tying it to a face.
/// </para>
/// <para>
/// <b>A critical is its own roll, and that is the whole of the bug report that started this.</b> It
/// used to be a natural 20, which is <em>also</em> the only face that beat a maxed-out defence — so
/// a well-guarded defender stopped taking ordinary blows and took nothing but doubled ones. A level
/// 48 saw a level 28 mob, still worth a fifth of full experience, land only criticals. Rolling the
/// critical separately makes its share of landed blows constant at every defence gap, which is what
/// everyone assumed it already was.
/// </para>
/// </remarks>
public static class DamageCalculator
{
    /// <summary>
    /// How much better than their raw rating a trained character swings.
    /// </summary>
    /// <remarks>
    /// Solved against a target rather than chosen: it puts a level-appropriate player at about 81%
    /// to land a swing, which is roughly where fights sat before and so keeps kill times near what
    /// the content was authored against. It is the character's half of an asymmetry the game
    /// already had — mobs used to carry a flat <c>+6</c> for the same reason, standing for
    /// competence rather than for level.
    /// </remarks>
    public const decimal CharacterSkill = 1.75m;

    /// <summary>How much better than its raw level a mob swings.</summary>
    /// <remarks>
    /// The mob's share of the same asymmetry, solved against about 52% for a level-appropriate mob
    /// against a level-appropriate player. Raising it makes every fight in the game bloodier; that
    /// is what it is for.
    /// </remarks>
    public const decimal MobSkill = 1.25m;

    /// <summary>
    /// The floor and ceiling on a swing's chance to land. Keeping both ends open is what makes
    /// "never certain either way" a property of the system rather than of its content.
    /// </summary>
    public const double MinHitChance = 0.05;

    /// <inheritdoc cref="MinHitChance"/>
    public const double MaxHitChance = 0.95;

    /// <summary>
    /// The chance that a blow which landed is a critical.
    /// </summary>
    /// <remarks>
    /// One in twenty, which is what a natural 20 was worth back when landing a hit was nearly
    /// certain — so a well-matched fight feels the way it always did. The difference is at the
    /// other end: this is a share of <em>landed</em> blows rather than of all swings, so a defender
    /// who is hard to hit now takes fewer criticals instead of nothing but.
    /// </remarks>
    public const double CriticalChance = 0.05;

    /// <summary>
    /// What a point of gear or attribute is worth, as a fraction of the bearer's own level.
    /// </summary>
    /// <remarks>
    /// <b>Gear is a percentage of level, not a number added to it, because a flat bonus means
    /// something different at every level.</b> A shield worth <c>+5</c> was the whole of a level 5
    /// character's evasion and a rounding error on a level 48's — the same authored number, two
    /// unrelated effects, and nothing a builder could aim at. Measured, it broke the model at the
    /// bottom of the game: a level 5 in ordinary gear could be hit by a level 2 mob — still worth a
    /// quarter of full experience — only 5% of the time, the same wall this replaced at the top.
    ///
    /// Thirty, so that a full set and a maxed attribute together are worth a little under half
    /// again, and the strongest guard in the game roughly doubles evasion without ever reaching the
    /// clamp. Armour carries the rest of the gear progression, on the damage side, where it can
    /// scale in absolute terms because <see cref="ArmorCurve"/> measures it against the attacker.
    /// </remarks>
    public const decimal GearScale = 30m;

    /// <summary>
    /// How hard this defender is to land a blow on: their level, raised by whatever gear,
    /// attributes and guards they carry.
    /// </summary>
    /// <remarks>
    /// Floored at zero. A negative rating is a real case — a low Agility modifier, or an expose
    /// that strips a guard past nothing — and it must land as "no defence at all" rather than
    /// wrapping back into one when <see cref="HitChance"/> squares it.
    /// </remarks>
    public static double Evasion(DefenderStats defender)
    {
        ArgumentNullException.ThrowIfNull(defender);

        var scaled = defender.Level * (1m + (defender.DefenseRating / GearScale));

        return Math.Max(0d, (double)scaled);
    }

    /// <summary>
    /// The chance this attacker lands a blow on this defender, in
    /// <c>[<see cref="MinHitChance"/>, <see cref="MaxHitChance"/>]</c>.
    /// </summary>
    /// <remarks>
    /// <b>Public because the <c>stats</c> screen shows it and the balance harness measures it.</b>
    /// Both would otherwise add the terms up themselves, which is the duplication that once let
    /// that screen advertise a damage range combat would never roll.
    ///
    /// Both sides are floored at zero before squaring — see <see cref="Evasion"/> for why that
    /// matters on the defending side.
    /// </remarks>
    public static double HitChance(AttackerStats attacker, DefenderStats defender)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(defender);

        var accuracy = (double)Math.Max(0, attacker.AttackRating);
        var evasion = Evasion(defender);

        if (accuracy <= 0)
        {
            return MinHitChance;
        }

        if (evasion <= 0)
        {
            return MaxHitChance;
        }

        var power = accuracy * accuracy;
        var guard = evasion * evasion;

        return Math.Clamp(power / (power + guard), MinHitChance, MaxHitChance);
    }

    /// <summary>
    /// Calculate damage for one attack.
    /// </summary>
    public static DamageResult CalculateDamage(
        AttackerStats attacker,
        DefenderStats defender,
        IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(defender);
        ArgumentNullException.ThrowIfNull(random);

        var chance = HitChance(attacker, defender);

        if (!random.Chance(chance))
        {
            return new DamageResult(Hit: false, IsCritical: false, DamageDealt: 0, HitChance: chance);
        }

        // Rolled only on a blow that landed, so the critical rate is a share of hits rather than of
        // swings. See the remarks on this class for what the other way round did.
        var isCrit = random.Chance(CriticalChance);

        var damageRolled = random.Next(attacker.MinDamage, attacker.MaxDamage + 1);

        // On a crit the dice are rolled twice and summed, with the flat modifier added once.
        // Taking the better of the two rolls instead, as this once did, moves the average so little
        // that a critical was indistinguishable from an ordinary hit at low weapon dice.
        if (isCrit)
        {
            damageRolled += random.Next(attacker.MinDamage, attacker.MaxDamage + 1);
        }

        var totalDamage = damageRolled + attacker.BaseDamage;

        // Armor absorbs a fraction, never a fixed amount, and the fraction is read against the
        // attacker's level. See ArmorCurve for why.
        var absorbed = ArmorCurve.Mitigation(defender.Armor, attacker.Level, defender.MitigationDelta);
        var final = (int)((decimal)totalDamage * (1m - absorbed));

        // Never drop below 1 damage on a hit
        final = Math.Max(1, final);

        return new DamageResult(Hit: true, IsCritical: isCrit, DamageDealt: final, HitChance: chance);
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
        var level = FightingLevel(mob);

        // A mob's accuracy is its level, with its competence in the skill factor beside it. This
        // was `level/2 + 6` when the number had to fit inside a d20 alongside a base of 10; there
        // is no budget to fit inside any more, so the plain level is what it means.
        var rawAccuracy = StatReader.TryReadInt(stats, "attackRating", out var rating) ? rating : level;
        var attackRating = (int)Math.Round(MobSkill * rawAccuracy, MidpointRounding.AwayFromZero);

        // Zero, not level/3. All of the level scaling lives in the dice below, so there is one
        // place to read it and an authored `damage` means exactly what it says rather than
        // silently acquiring an adder that grows.
        StatReader.TryReadInt(stats, "baseDamage", out var baseDamage);

        // The dice grow with level; they used to be a fixed 1-4 with a level/3 adder beside them.
        // Two things were wrong with that. A level 50 mob dealt 17-20, which is an eight percent
        // spread - very nearly a fixed number, so no swing in the whole exchange was luckier than
        // any other. And the total fell steadily behind: fights got *longer* as the game went on,
        // from about thirty landed blows to kill a player at level 1 to fifty-three at level 50,
        // because player health and mitigation both outgrew it.
        //
        // Scaling every face keeps it a d4 in shape - the spread stays roughly three or four to
        // one at every level - while the average tracks what a character of that level can absorb.
        var minDamage = 1 + (level / 2);
        var maxDamage = 4 + (3 * level / 2);

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
            Level: level,
            AttackRating: attackRating,
            BaseDamage: baseDamage,
            MinDamage: minDamage,
            MaxDamage: maxDamage);
    }

    /// <summary>
    /// The level a mob fights at, for the stats its template leaves to the level to decide.
    /// </summary>
    /// <remarks>
    /// <b>Effective, not authored.</b> These fallbacks are what most mobs actually run on — a
    /// template that declares no <c>attackRating</c>, <c>baseDamage</c> or <c>defense</c> is the
    /// common case, and reading <see cref="Mob.Level"/> here meant such a mob fought at its
    /// authored level no matter what its zone had done to it. Its health pool scaled and its punch
    /// did not.
    ///
    /// Zero means the mob never went through the spawner — a test, or something hand-built — and
    /// its authored level is then the only level there is.
    /// </remarks>
    private static int FightingLevel(Mob mob) =>
        mob.EffectiveLevel > 0 ? mob.EffectiveLevel : mob.Level;

    /// <summary>
    /// Build defender stats from a mob, preferring what its template declares and falling back
    /// to level-derived defence for anything it leaves out.
    /// </summary>
    public static DefenderStats DefenderStatsFrom(Mob mob)
    {
        ArgumentNullException.ThrowIfNull(mob);

        var stats = mob.ResolvedStats;

        // Zero by default. The mob's own level is already the bulk of its evasion, so a template
        // that says nothing means "no harder to hit than its level already makes it" rather than
        // quietly acquiring a defence its author never wrote.
        StatReader.TryReadInt(stats, "defense", out var defense);
        StatReader.TryReadInt(stats, "armor", out var armor);

        return new DefenderStats(
            Level: FightingLevel(mob),
            DefenseRating: defense,
            Armor: armor);
    }
}
