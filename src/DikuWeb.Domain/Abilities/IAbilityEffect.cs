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

    /// <summary>Whether this is something a target would rather avoid.</summary>
    /// <remarks>
    /// Which way an ability points decides two things: what a bare <c>cast</c> with no target name
    /// falls back to - the thing you are fighting, or yourself - and who an area effect gathers up.
    /// Both used to read a hardcoded list of two effect keys in the command layer, which meant the
    /// five executors added after it were all classified as helpful: casting Scorch with no target
    /// named set the caster on fire.
    ///
    /// Declared on the executor because the executor is the thing that knows. A new one cannot be
    /// written without answering the question.
    /// </remarks>
    bool IsHarmful { get; }

    /// <summary>
    /// Apply this effect to a target.
    /// </summary>
    /// <param name="caster">The character casting the ability.</param>
    /// <param name="target">The target (character or mob), or null for self/AoE context.</param>
    /// <param name="parameters">Ability-specific parameters as JSON strings: e.g., scalingFactor, minDamage.</param>
    /// <param name="random">RNG for variance (critical hits, etc.).</param>
    void Apply(
        object caster,
        object? target,
        Dictionary<string, string> parameters,
        IRandomSource random);
}
