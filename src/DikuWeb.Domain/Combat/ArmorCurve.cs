namespace DikuWeb.Domain.Combat;

/// <summary>
/// Turns an armor rating into the fraction of a blow it absorbs (PLAN.md §4.6).
/// </summary>
/// <remarks>
/// <para>
/// <b>A fraction rather than a subtraction, because subtraction has no usable value.</b> Armor used
/// to be <c>armorFlat</c>, taken off each hit before a 1-damage floor, and that is all-or-nothing at
/// every level: 10 flat reduced a level 25 mob's blow to the floor and a level 50 mob's by less
/// than half. There is no number that behaves reasonably across even one band, because the thing it
/// is subtracted from grows and it does not.
/// </para>
/// <para>
/// <b>Measured against the attacker, not against a constant.</b> The denominator used to be
/// <c>armor + 100</c>, and a global constant makes an armor point mean something different at every
/// tier: measured across authored content, a full set absorbed 20% in Ossara, 35% in Grask, 49% in
/// Azhen and 60% in Nemhal, walking steadily toward the cap. Authors had to inflate ratings to keep
/// pace with a number none of them could see, and the cap — meant as a safety rail against absurd
/// input — was becoming a design ceiling that endgame content would simply sit on.
/// </para>
/// <para>
/// Content is already authored on a linear curve, about <c>3.5 × level</c> for a full set, so
/// dividing by <see cref="Bite"/> × the attacker's level holds a level-appropriate set at a
/// constant ~40% at every tier. It also gives armour the property the old form could not: the same
/// set absorbs <em>more</em> from a weaker attacker and less from a stronger one, so gear earned at
/// risk keeps paying against everything you have already outgrown without ever making you immune to
/// the things that still reward you.
/// </para>
/// <para>
/// <b>It cannot reach 1, and is capped below that anyway.</b> <c>A / (A + K)</c> approaches total
/// immunity without ever arriving, so no authored number — including a builder's mistyped extra
/// zero — can produce a character nothing can hurt. <see cref="Cap"/> then stops it well short, so
/// even a perfectly equipped character takes a quarter of every blow that lands.
/// </para>
/// </remarks>
public static class ArmorCurve
{
    /// <summary>
    /// How hard a level of attacker bites through armour. An armor rating of
    /// <c>Bite × attackerLevel</c> absorbs exactly half the blow, which is the one sentence every
    /// authored armour value is chosen against.
    /// </summary>
    /// <remarks>
    /// Five, solved against the content that exists: a full set is authored at roughly
    /// <c>3.5 × level</c>, so a level-appropriate set lands at <c>3.5 / (3.5 + 5)</c> — about 41%,
    /// which is where the mid-game already sat before the tier drift took hold.
    /// </remarks>
    public const int Bite = 5;

    /// <summary>
    /// The most any amount of armor can absorb. The remainder is what keeps a fight a fight.
    /// </summary>
    public const decimal Cap = 0.75m;

    /// <summary>
    /// The fraction of a blow absorbed by this armor rating against an attacker of this level,
    /// in <c>[0, <see cref="Cap"/>]</c>.
    /// </summary>
    /// <param name="armor">Summed armor rating. Negative or zero absorbs nothing.</param>
    /// <param name="attackerLevel">
    /// The level the attacker fights at. Floored at 1: a level 0 attacker is a test fixture or a
    /// mob that never went through the spawner, and dividing by its level would make armour
    /// absolute rather than throwing where anyone would notice.
    /// </param>
    /// <remarks>
    /// The denominator is summed in <see cref="decimal"/> on purpose: <c>int.MaxValue + Bite</c>
    /// overflows to a negative, which turned the largest armor rating expressible into a *negative*
    /// mitigation and so into bonus damage for the attacker. Exactly the class of absurd input this
    /// curve exists to be safe against.
    /// </remarks>
    public static decimal Mitigation(int armor, int attackerLevel) =>
        armor <= 0
            ? 0m
            : Math.Min(Cap, armor / ((decimal)armor + (Bite * Math.Max(1, attackerLevel))));

    /// <summary>
    /// The same fraction with temporary effects folded in, clamped once at the end.
    /// </summary>
    /// <remarks>
    /// Guard effects carry percentage points rather than armor rating
    /// (<c>ActiveEffect.MitigationDelta</c>), so that a shout worth "ten percent less damage" is
    /// worth that at every tier instead of being decisive at level 5 and rounding to nothing at
    /// level 50. Clamping after the sum rather than before it is what stops a stack of buffs from
    /// exceeding the cap the gear alone respects.
    /// </remarks>
    public static decimal Mitigation(int armor, int attackerLevel, decimal delta) =>
        Math.Clamp(Mitigation(armor, attackerLevel) + delta, 0m, Cap);
}
