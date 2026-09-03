namespace Muwbta.Domain.Abilities.Effects;

/// <summary>
/// An ability effect that applies a timed modifier to one or more targets.
/// Extends IAbilityEffect with the ability to create ongoing ActiveEffect state.
/// </summary>
public interface IBuffEffect : IAbilityEffect
{
    /// <summary>
    /// Create an active effect instance to be tracked by WorldState.
    /// Called during AbilitySystem.ResolveCast after effect.Apply().
    /// </summary>
    ActiveEffect CreateActiveEffect(
        object caster,
        object? target,
        Dictionary<string, string> parameters,
        long currentPulse);
}
