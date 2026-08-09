namespace DikuWeb.Domain.Abilities.Effects;

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

    /// <summary>Outgoing damage multiplier (default 1.0 = no change).</summary>
    public decimal OutgoingDamageMultiplier { get; init; } = 1.0m;

    /// <summary>Incoming damage multiplier (default 1.0 = no change).</summary>
    public decimal IncomingDamageMultiplier { get; init; } = 1.0m;

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
