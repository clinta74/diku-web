namespace DikuWeb.Domain.Combat;

/// <summary>
/// Turns an armor rating into the fraction of a blow it absorbs (PLAN.md §4.6).
/// </summary>
/// <remarks>
/// <para>
/// <b>A curve rather than a subtraction, because subtraction has no usable value.</b> Armor used to
/// be <c>armorFlat</c>, taken off each hit before a 1-damage floor, and that is all-or-nothing at
/// every level: 10 flat reduced a level 25 mob's blow to the floor and a level 50 mob's by less
/// than half. There is no number that behaves reasonably across even one band, because the thing it
/// is subtracted from grows and it does not. A fraction is scale-free by construction, which is the
/// property flat reduction can never have.
/// </para>
/// <para>
/// <b>It cannot reach 1, and is capped below that anyway.</b> <c>A / (A + K)</c> approaches total
/// immunity without ever arriving, so no authored number — including a builder's mistyped extra
/// zero — can produce a character nothing can hurt. <see cref="Cap"/> then stops it well short, so
/// even a perfectly equipped character takes a quarter of every blow that lands. That is the half
/// of "armor must not make you untouchable" that mitigation is responsible for; the other half is
/// the roll clamp in <see cref="DamageCalculator"/>.
/// </para>
/// <para>
/// <b><see cref="Midpoint"/> is the only tuning number, and it reads as one sentence:</b> an armor
/// rating equal to it halves incoming damage. Every item value in the game is chosen against that
/// sentence, which is what keeps a realm's set a single decision (WORLD.md §7.3) rather than a
/// per-piece negotiation.
/// </para>
/// </remarks>
public static class ArmorCurve
{
    /// <summary>The armor rating at which exactly half of each blow is absorbed.</summary>
    public const int Midpoint = 100;

    /// <summary>
    /// The most any amount of armor can absorb. The remainder is what keeps a fight a fight.
    /// </summary>
    public const decimal Cap = 0.75m;

    /// <summary>
    /// The fraction of a blow absorbed by this armor rating, in <c>[0, <see cref="Cap"/>]</c>.
    /// </summary>
    /// <param name="armor">Summed armor rating. Negative or zero absorbs nothing.</param>
    /// <remarks>
    /// The denominator is summed in <see cref="decimal"/> rather than <see cref="int"/> on purpose:
    /// <c>int.MaxValue + Midpoint</c> overflows to a negative, which turned the largest armor rating
    /// expressible into a *negative* mitigation and so into bonus damage for the attacker. Exactly
    /// the class of absurd input this curve exists to be safe against.
    /// </remarks>
    public static decimal Mitigation(int armor) =>
        armor <= 0 ? 0m : Math.Min(Cap, armor / ((decimal)armor + Midpoint));

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
    public static decimal Mitigation(int armor, decimal delta) =>
        Math.Clamp(Mitigation(armor) + delta, 0m, Cap);
}
