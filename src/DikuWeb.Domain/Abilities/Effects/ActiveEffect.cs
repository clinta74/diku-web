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
