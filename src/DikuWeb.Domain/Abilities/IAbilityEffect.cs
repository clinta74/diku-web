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
    /// What this effect does, in one phrase, from the parameters it is about to be given.
    /// </summary>
    /// <param name="parameters">The same dictionary <see cref="Apply"/> would be handed.</param>
    /// <param name="targeting">Who the ability aims at, for the words to use about them.</param>
    /// <returns>
    /// A lower-case phrase with no full stop - "deals 10-14 damage to your target" - so several can
    /// be joined into one line for an ability that does several things.
    /// </returns>
    /// <remarks>
    /// <b>On the executor because the executor is the thing that knows</b> - the same argument
    /// <see cref="IsHarmful"/> makes, for the same reason. An effect reads its parameters by name
    /// and skips what it does not recognise, so the only code that can say what a dial is worth is
    /// the code that reads it. A describer written elsewhere would be a second copy of every
    /// formula, free to drift from the one that runs.
    ///
    /// <b>Describe what happens, not what was meant.</b> Where an executor clamps a value, or
    /// floors it, or ignores it, the phrase says the clamped number - a description that reported
    /// the authored one would be a screen disagreeing with the game, which is the failure this
    /// codebase keeps finding.
    ///
    /// A new executor cannot be written without answering the question, which is the point of
    /// putting it here rather than in a lookup that would quietly have no entry.
    /// </remarks>
    string Describe(Dictionary<string, string> parameters, TargetingType targeting);

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
