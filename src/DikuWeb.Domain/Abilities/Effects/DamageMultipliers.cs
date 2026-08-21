namespace DikuWeb.Domain.Abilities.Effects;

/// <summary>
/// What the buffs and debuffs currently on two combatants do to the damage between them.
/// </summary>
/// <remarks>
/// <para>
/// <b>This lived inside the weapon strike path, and so did the whole feature.</b> The multipliers
/// were read in one place — <c>CombatSystem</c>, mid-swing — which meant that for seventeen
/// abilities the number a player was shown was a number only their auto-attacks honoured. An Adept's
/// Arcane Surge described itself as <em>"raises your damage by 60%"</em> and moved nothing an Adept
/// actually casts; Sunder's <em>"raises the damage your target takes by 30%"</em> did nothing to the
/// abilities landed on the sundered mob. Nothing was broken, nothing threw, and the descriptions
/// were generated from the same authored numbers the effects carried — so the field was right, the
/// wording was right, and it reached one of the two places it belonged.
/// </para>
/// <para>
/// Here rather than in the Engine because it is a pure function of a list of effects, with no world
/// and no combat in it — the same reasoning that put <c>AbilityCooldowns</c> in the Domain. Both
/// callers (a swing, an ability) need the identical rule, and a rule copied into two places is a
/// rule that will be fixed in one of them.
/// </para>
/// <para>
/// <b>Armour is deliberately not here.</b> A blow is stopped by what a target is wearing; a curse is
/// not, which is why a bleed's damage is not run through <c>ArmorCurve</c>. If curse mitigation is
/// ever wanted it wants its own stat rather than a second meaning for the one that already exists.
/// See PLAN.md §4.6.
/// </para>
/// </remarks>
public static class DamageMultipliers
{
    /// <summary>Neither helped nor hindered.</summary>
    public const decimal None = 1.0m;

    /// <summary>How much more damage the bearer of these effects deals.</summary>
    public static decimal Outgoing(IEnumerable<ActiveEffect> effects) =>
        Product(effects, e => e.OutgoingDamageMultiplier);

    /// <summary>How much more damage the bearer of these effects takes.</summary>
    public static decimal Incoming(IEnumerable<ActiveEffect> effects) =>
        Product(effects, e => e.IncomingDamageMultiplier);

    /// <summary>
    /// The whole multiplier on damage travelling from one combatant to the other.
    /// </summary>
    /// <remarks>
    /// A product rather than a sum, so a shout of fury and a curse of weakness compose rather than
    /// one overwriting the other — and so the order they were cast in cannot matter.
    /// </remarks>
    public static decimal Between(
        IEnumerable<ActiveEffect> attackerEffects,
        IEnumerable<ActiveEffect> targetEffects) =>
        Outgoing(attackerEffects) * Incoming(targetEffects);

    /// <summary>Applies a multiplier to a damage number, as damage rather than as arithmetic.</summary>
    /// <remarks>
    /// <b>Away from zero</b>, so a 1-damage hit under a small buff rounds up to 2 rather than
    /// banker's-rounding back to itself — the tiny numbers are exactly where a player is most likely
    /// to conclude the buff does nothing. Never below zero: a debuff strong enough to invert damage
    /// would otherwise heal what it was cast at.
    /// </remarks>
    public static int Apply(int damage, decimal multiplier) =>
        multiplier == None
            ? damage
            : Math.Max(0, (int)Math.Round(damage * multiplier, MidpointRounding.AwayFromZero));

    /// <summary>
    /// One side's multiplier, across every effect it is carrying.
    /// </summary>
    /// <remarks>
    /// <b>A stacking effect scales its <em>bonus</em>, not itself.</b> Three stacks of a 1.2 is
    /// 1.6 (<c>1 + 0.2 × 3</c>) and not 1.728 — compounding a stack against itself is how a bleed
    /// meant to be worth twenty percent becomes worth seventy at five stacks. Anything not on
    /// <see cref="EffectStackingRule.Stack"/> holds one application however many times it was
    /// re-cast, which is what <c>Refresh</c> means.
    /// </remarks>
    private static decimal Product(
        IEnumerable<ActiveEffect> effects,
        Func<ActiveEffect, decimal> selector)
    {
        ArgumentNullException.ThrowIfNull(effects);
        ArgumentNullException.ThrowIfNull(selector);

        var multiplier = None;

        foreach (var effect in effects)
        {
            var value = selector(effect);

            // Skipped rather than multiplied by one. Most effects in a fight are neither a damage
            // buff nor a damage debuff, and 1.0m * 1.0m repeated over a stack of them is arithmetic
            // that can only lose precision.
            if (value == None)
            {
                continue;
            }

            multiplier *= effect.StackingRule == EffectStackingRule.Stack
                ? None + ((value - None) * effect.Stacks)
                : value;
        }

        return multiplier;
    }
}
