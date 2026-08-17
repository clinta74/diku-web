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
/// <param name="Level">
/// The level the defender fights at. Present because the number an attacker must beat scales with
/// it at the same rate <see cref="AttackerStats.AttackRating"/> does, which is what keeps the die
/// deciding the outcome — see <see cref="DamageCalculator"/>.
/// </param>
/// <param name="DefenseRating">How much harder than baseline this defender is to hit.</param>
/// <param name="Armor">
/// Summed armor rating, converted to a fraction by <see cref="ArmorCurve"/>. Replaced the old
/// <c>ArmorFlat</c>/<c>ArmorPercent</c> pair: one number the author sets, one curve that reads it.
/// </param>
public sealed record DefenderStats(
    int Level,
    int DefenseRating,
    int Armor);

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
///   defenseVal  = 10 + level/2 + defenseRating
///   needed      = clamp(defenseVal − attackRating, 2, 20)
///   miss   if natural d20 &lt; needed
///   hit    if natural d20 ≥ needed
///   crit   if natural 20
///
///   damage = roll weapon dice + MightMod (passed as baseDamage)
///   final  = max(1, damage × (1 − ArmorCurve.Mitigation(armor)))
///
/// No world state, no side effects. Given the same inputs and random seed,
/// always returns the same result.
/// </summary>
/// <remarks>
/// <para>
/// <b>Both sides carry level/2, and that is the whole repair.</b> Attack rating grew at
/// <c>level/2</c> while the number to beat grew at <c>level/4</c>, so the gap between them widened
/// past the die's entire range: by level 15 a player hit every swing, by level 30 so did every mob,
/// and the d20 had stopped being consulted. Putting the same <c>level/2</c> on the defence side
/// cancels it exactly at equal levels, leaving gear, attributes and the level *difference* to
/// decide — all of which are small enough for twenty faces to express.
/// </para>
/// <para>
/// <b>The clamp is the guarantee, not the tuning.</b> Clamping the needed roll to 2–20 means a
/// natural 1 always misses and a natural 20 always hits, whatever anyone authors. Nothing can be
/// equipped or buffed into being unhittable, and nothing can be stripped into being auto-hit, so
/// those two properties stop depending on anybody's numbers being sensible.
/// </para>
/// <para>
/// <b>A critical is a natural 20 and nothing else.</b> It used to also trigger on beating the
/// defence by ten or more, which was fine while overshoot stayed small and became absurd once it
/// did not: at level 50 a mob's every landed blow overshot by ten, so every hit was a critical and
/// the dice were rolled twice, permanently. Overshoot is a symptom of the scaling above, so a rule
/// that reads it was measuring the bug.
/// </para>
/// </remarks>
public static class DamageCalculator
{
    /// <summary>The number an unlevelled, undefended target is hit on. The d20 baseline.</summary>
    private const int BaseDefense = 10;

    /// <summary>
    /// The d20 faces that always miss and always hit. Keeping both ends open is what makes
    /// "never certain either way" a property of the system rather than of its content.
    /// </summary>
    private const int MinNeeded = 2;

    /// <inheritdoc cref="MinNeeded"/>
    private const int MaxNeeded = 20;

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

        var naturalRoll = random.Next(1, 21); // d20: 1-20
        var defenseVal = BaseDefense + (defender.Level / 2) + defender.DefenseRating;
        var needed = Math.Clamp(defenseVal - attacker.AttackRating, MinNeeded, MaxNeeded);

        if (naturalRoll < needed)
        {
            return new DamageResult(naturalRoll, false, false, 0);
        }

        var isCrit = naturalRoll == 20;

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

        // Armor absorbs a fraction, never a fixed amount. See ArmorCurve for why.
        var absorbed = ArmorCurve.Mitigation(defender.Armor);
        var final = (int)((decimal)totalDamage * (1m - absorbed));

        // Never drop below 1 damage on a hit
        final = Math.Max(1, final);

        return new DamageResult(naturalRoll, true, isCrit, final);
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

        // Level-derived defaults, used wherever the template is silent.
        var attackRating = StatReader.TryReadInt(stats, "attackRating", out var rating)
            ? rating
            : (level / 2) + MobAttackBaseline;

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
    /// How hard a mob swings before its template says otherwise.
    /// </summary>
    /// <remarks>
    /// A mob's rating is <c>level/2</c>, and the defence it faces now carries <c>level/2</c> too, so
    /// without this the two cancel and every mob in the game is left beating <c>10 + agility +
    /// gear</c> on a bare die — which a geared character wins outright. This is the mob's share of
    /// the baseline: it stands for competence rather than level, which is why it is a constant and
    /// not a rate. Raising it makes every fight in the game bloodier; that is what it is for.
    /// </remarks>
    private const int MobAttackBaseline = 6;

    /// <summary>
    /// Build defender stats from a mob, preferring what its template declares and falling back
    /// to level-derived defence for anything it leaves out.
    /// </summary>
    public static DefenderStats DefenderStatsFrom(Mob mob)
    {
        ArgumentNullException.ThrowIfNull(mob);

        var stats = mob.ResolvedStats;

        // Zero by default, not level/4. The level term now lives in the formula itself, so a
        // template that says nothing means "no harder to hit than its level already makes it"
        // rather than quietly acquiring a defence its author never wrote.
        StatReader.TryReadInt(stats, "defense", out var defense);
        StatReader.TryReadInt(stats, "armor", out var armor);

        return new DefenderStats(
            Level: FightingLevel(mob),
            DefenseRating: defense,
            Armor: armor);
    }
}
