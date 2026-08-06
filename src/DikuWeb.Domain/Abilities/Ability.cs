namespace DikuWeb.Domain.Abilities;

/// <summary>
/// An ability (spell, skill, technique) that a character can cast or use.
/// Abilities are defined in the database and characters learn them via level progression.
/// </summary>
public sealed class Ability
{
    /// <summary>Unique key: e.g., "warden.slash" or "adept.bolt".</summary>
    public required string Key { get; init; }

    /// <summary>Display name shown to players.</summary>
    public required string Name { get; init; }

    /// <summary>Flavor text describing the ability.</summary>
    public required string Description { get; init; }

    /// <summary>What resource this ability consumes: Focus, Stamina, or Health.</summary>
    public required CostType CostType { get; init; }

    /// <summary>How much of the cost resource to consume on cast.</summary>
    public required int CostValue { get; init; }

    /// <summary>Cooldown duration in pulses (250ms each). 0 = no cooldown.</summary>
    public required long CooldownPulses { get; init; }

    /// <summary>Time before effect lands, in pulses. Null or 0 = instant cast.</summary>
    public long? CastTimePulses { get; init; }

    /// <summary>Who or what can be targeted: self, single target, or AoE.</summary>
    public required TargetingType TargetingType { get; init; }

    /// <summary>Effect key: e.g., "damage.physical", "heal.restore". Resolved by EffectRegistry at runtime.</summary>
    public required string EffectKey { get; init; }

    /// <summary>Parameters passed to the effect executor as JSON strings: e.g., scalingFactor, minDamage, etc.</summary>
    public Dictionary<string, string> EffectParams { get; init; } = [];
}

/// <summary>Resource consumed by an ability.</summary>
public enum CostType
{
    Focus = 0,
    Stamina = 1,
    Health = 2,
}

/// <summary>Targeting mode for an ability.</summary>
public enum TargetingType
{
    /// <summary>Ability must target one entity (player specifies).</summary>
    SingleTarget = 0,

    /// <summary>Ability targets the caster only.</summary>
    Self = 1,

    /// <summary>Ability targets all occupants in the room (filters per target).</summary>
    Aoe = 2,
}
