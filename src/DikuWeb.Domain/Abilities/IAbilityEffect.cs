using DikuWeb.Domain.Randomness;

namespace DikuWeb.Domain.Abilities;

/// <summary>
/// Extensible ability effect executor. Each ability references an effect key,
/// which is resolved at runtime and applied to the target(s).
/// </summary>
public interface IAbilityEffect
{
    /// <summary>The effect key this executor handles: e.g., "damage.physical", "heal.restore".</summary>
    string EffectKey { get; }

    /// <summary>
    /// Apply this effect to a target.
    /// </summary>
    /// <param name="caster">The character casting the ability.</param>
    /// <param name="target">The target (character or mob), or null for self/AoE context.</param>
    /// <param name="parameters">Ability-specific parameters: e.g., scalingFactor, minDamage.</param>
    /// <param name="random">RNG for variance (critical hits, etc.).</param>
    void Apply(
        object caster,
        object? target,
        Dictionary<string, object> parameters,
        IRandomSource random);
}
