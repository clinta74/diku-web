namespace Muwbta.Domain.Abilities.Effects;

/// <summary>
/// A timed effect modifier on outgoing/incoming combat damage.
/// Applied to a character or mob, expired on the 60s regen tick.
/// In-memory-only, resets on restart (matches cooldown/combat transience).
/// </summary>
public sealed class ActiveEffect
{
    /// <summary>Effect key, e.g. "buff.battle-fury", "debuff.weaken".</summary>
    public required string EffectKey { get; init; }

    /// <summary>Display name for narration and UI.</summary>
    public required string Name { get; init; }

    /// <summary>Caster entity ID: "c_<guid>" (character) or "m_<guid>" (mob).</summary>
    public required string SourceEntityId { get; init; }

    /// <summary>
    /// The unlock level of the ability that applied this, and so how strong an application it is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What decides which of two colliding applications survives.</b> <c>WorldState.ApplyEffect</c>
    /// dedupes on (<see cref="EffectKey"/>, <see cref="SourceEntityId"/>), so every one of a Path's
    /// maximum-health buffs collides with the rest — and refreshing kept the <em>first</em>
    /// magnitude, which made Sanctuary over Fortitude worth +150 rather than +220 and let a cheap
    /// Ambush hold a Hemorrhage-sized bleed open forever. A higher level now replaces outright and a
    /// lower one is ignored.
    /// </para>
    /// <para>
    /// <b>Level rather than magnitude</b>, because these effects measure themselves in health,
    /// defence, mitigation, tick damage and multipliers, and a field-by-field comparison would have
    /// to decide whether +12 defence and 9% beats +10 and 12%. The level is the one scalar that means
    /// the same thing for all of them.
    /// </para>
    /// <para>
    /// <b>Zero means no ability was behind it</b>, which is every mob attack rider. Two of those
    /// compare equal and refresh each other exactly as they always did, so no tuned fight changes.
    /// </para>
    /// <para>
    /// Settable rather than <c>init</c>, like <see cref="ExpiresAtPulse"/> and <see cref="Stacks"/>:
    /// it is stamped by <c>AbilitySystem</c> after the executor builds the effect, because which
    /// ability invoked an executor is none of the executor's business — and threading it through
    /// <c>IBuffEffect.CreateActiveEffect</c> would change eight implementors to serve one comparison.
    /// </para>
    /// </remarks>
    public int SourceUnlockLevel { get; set; }

    /// <summary>Outgoing damage multiplier (default 1.0 = no change).</summary>
    public decimal OutgoingDamageMultiplier { get; init; } = 1.0m;

    /// <summary>Incoming damage multiplier (default 1.0 = no change).</summary>
    public decimal IncomingDamageMultiplier { get; init; } = 1.0m;

    /// <summary>
    /// Added to the bearer's defence rating, which is what an attack roll has to beat. Negative
    /// makes them easier to hit.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="IncomingDamageMultiplier"/> because they answer different
    /// questions: this changes how often a blow lands, that changes how much it costs when one
    /// does. A shield wall should do the first; being vulnerable is the second.
    /// </remarks>
    public int DefenseRatingDelta { get; init; }

    /// <summary>
    /// Added to the fraction of each landed blow the bearer's armour absorbs, as a decimal where
    /// <c>0.10</c> is ten percentage points.
    /// </summary>
    /// <remarks>
    /// <b>Percentage points rather than armour rating, so a shout is worth the same at every
    /// tier.</b> This was a flat amount subtracted from each blow, which made a guard worth roughly
    /// a whole hit at level 5 and a rounding error at level 50. Adding to the bearer's armour rating
    /// instead would have been no better: the curve's returns diminish, so the same grant would be
    /// worth twenty points to an unarmoured Adept and two to a geared Warden — backwards, since the
    /// Warden is the one whose abilities these are.
    ///
    /// Summed across effects and clamped once, by <see cref="Combat.ArmorCurve.Mitigation(int, decimal)"/>,
    /// so a stack of guards still cannot exceed the cap gear alone respects.
    /// </remarks>
    public decimal MitigationDelta { get; init; }

    /// <summary>
    /// Raises the bearer's maximum health while this is active, and lowers it again when it goes.
    /// </summary>
    /// <remarks>
    /// The grant of current health happens once, when the effect is first applied - never on a
    /// refresh. Otherwise re-casting is a heal on a short cooldown wearing a buff's clothes, which
    /// is exactly what the ability this was built for was already being written as.
    /// </remarks>
    public int MaxHealthDelta { get; init; }

    /// <summary>
    /// Damage dealt each time this effect ticks. Zero for anything that is not a bleed or a burn.
    /// </summary>
    public int TickDamage { get; init; }

    /// <summary>
    /// Pulses between ticks. Zero means this effect never ticks, whatever <see cref="TickDamage"/>
    /// says - a tick interval of zero would otherwise fire every pulse.
    /// </summary>
    public long TickIntervalPulses { get; init; }

    /// <summary>The next pulse this effect deals its damage on.</summary>
    public long NextTickPulse { get; set; }

    /// <summary>True when this effect deals damage over time rather than only scaling it.</summary>
    public bool Ticks => TickDamage > 0 && TickIntervalPulses > 0;

    /// <summary>
    /// While true the bearer takes no turn: no swings, no casts, and any cast in progress breaks.
    /// </summary>
    /// <remarks>
    /// A flag on the effect rather than a field on the character, so it expires through the same
    /// sweep as everything else and cannot be left set by a code path that forgot to clear it.
    /// </remarks>
    public bool PreventsActing { get; init; }

    /// <summary>
    /// While true the bearer cannot leave: no walking out, and no fleeing a fight.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="PreventsActing"/> rather than a degree of it. A stun takes the
    /// turn and leaves the exit open; a snare leaves the turn and closes the exit. An ability can
    /// reasonably want either without the other.
    /// </remarks>
    public bool PreventsEscape { get; init; }

    /// <summary>Pulse at which this effect expires and is removed.</summary>
    public long ExpiresAtPulse { get; set; }

    /// <summary>Current stack count (default 1).</summary>
    public int Stacks { get; set; } = 1;

    /// <summary>Maximum stacks allowed (default 1, no stacking).</summary>
    public int MaxStacks { get; init; } = 1;

    /// <summary>Rule for how re-casting while active behaves.</summary>
    public EffectStackingRule StackingRule { get; init; } = EffectStackingRule.Refresh;
}

public enum EffectStackingRule
{
    /// <summary>Re-casting resets ExpiresAtPulse, no stack growth.</summary>
    Refresh,

    /// <summary>Each cast adds a stack (up to MaxStacks), multipliers scale with Stacks.</summary>
    Stack,

    /// <summary>A second cast while one is active does nothing.</summary>
    Ignore,
}
