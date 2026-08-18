using DikuWeb.Domain.Abilities.Effects;

namespace DikuWeb.Domain.Abilities;

/// <summary>
/// What an ability does, in one line, worked out from the dials it is authored with.
/// </summary>
/// <remarks>
/// <para>
/// <b>Derived rather than written, so it cannot be wrong.</b> <see cref="Ability.Description"/> is
/// authored flavour - "a thrown splinter of raw force" - and it is the wrong thing to answer the
/// question a player is asking when they type <c>abilities</c>. Worse, it is free to disagree with
/// the ability: a builder who halves a heal has no reason to remember the sentence, and from then
/// on the screen says one thing and the game does another. A line built from the parameters is
/// re-derived every time it is shown and has nothing to drift from.
/// </para>
/// <para>
/// The phrases come from the executors themselves - see <see cref="IAbilityEffect.Describe"/> -
/// because the executor is the only code that knows which parameters it reads, what it defaults
/// them to, and where it clamps them. This class only joins them up.
/// </para>
/// </remarks>
public static class AbilityDescriber
{
    /// <summary>
    /// One line for one ability: every effect it applies, in the order they land.
    /// </summary>
    /// <returns>
    /// A lower-case phrase with no full stop, so it can follow an em dash on a list line.
    /// </returns>
    public static string Describe(Ability ability, EffectRegistry effects)
    {
        ArgumentNullException.ThrowIfNull(ability);
        ArgumentNullException.ThrowIfNull(effects);

        if (ability.Effects.Count == 0)
        {
            // AbilityValidator calls this an error, so it should not reach a player - but if it
            // does, the honest line is the one that gets it reported.
            return "does nothing";
        }

        var phrases = ability.Effects
            .Select(spec => Describe(spec, ability.TargetingType, effects))
            .ToList();

        // Joined with "and" rather than a comma throughout, because every effect lands: an ability
        // is a list of things it does, not a choice between them.
        return phrases.Count == 1
            ? phrases[0]
            : string.Join(", and ", phrases);
    }

    /// <summary>One effect's phrase, or a legible complaint if nothing can run it.</summary>
    /// <remarks>
    /// An unregistered effect key is the most expensive mistake in an ability - the cast succeeds,
    /// the cost is spent, the cooldown starts, and nothing happens. Saying so in the listing puts
    /// it in front of the person best placed to report it, which is the same reason
    /// <c>AbilityCommands</c> already names an ability key with no row behind it.
    /// </remarks>
    private static string Describe(
        AbilityEffectSpec spec,
        TargetingType targeting,
        EffectRegistry effects)
    {
        var effect = effects.Get(spec.Key);

        return effect is null
            ? $"does nothing — '{spec.Key}' is not a known effect"
            : effect.Describe(spec.Params, targeting);
    }
}
