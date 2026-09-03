using Muwbta.Domain.Randomness;

namespace Muwbta.Domain.Abilities.Effects;

/// <summary>
/// An effect that rewrites a mob's hate list rather than changing anyone's numbers.
/// </summary>
/// <remarks>
/// Applied by <c>AbilitySystem</c> rather than by <see cref="IAbilityEffect.Apply"/>, the same
/// way <see cref="IBuffEffect"/> is: an executor is handed a caster and a target and nothing
/// else, and threat lives on the room's <c>Combat</c>. Widening the executor signature to carry a
/// combat would hand every effect in the game access to the hate list to serve the one that
/// needs it.
/// </remarks>
public interface IThreatEffect
{
    /// <summary>
    /// How big a lead to take over the current top hater, as a fraction of the target's maximum
    /// health.
    /// </summary>
    /// <remarks>
    /// Measured against the <em>target</em> rather than as a flat number, because threat is
    /// cumulative damage and so grows without bound as a fight runs. A flat lead of fifty would
    /// be decisive in the first ten seconds of a fight and beneath notice five minutes in, and it
    /// would mean nothing consistent between a rat and a dragon. A fraction of the health bar is
    /// the same promise everywhere: <b>to take this mob back off you, someone has to deal this
    /// much of its health in damage more than you do.</b>
    /// </remarks>
    decimal LeadFraction(Dictionary<string, string> parameters);
}

/// <summary>
/// Taunt. Puts the caster at the top of the target's hate list and makes it fight them.
/// </summary>
/// <remarks>
/// The first thing in the game that <em>writes</em> threat instead of earning it, which is the
/// whole of what a taunt is: every other route to the top of a hate list runs through dealing
/// more damage than anyone else. It exists because the alternative is a party where the only
/// viable damage is damage small enough not to pull the mob - which is a tax on the Adept for
/// doing the thing the Adept is for.
///
/// <b>A lead, not a lock.</b> The list stays a damage meter afterwards, so whoever was displaced
/// climbs back by out-damaging the taunter from here. A taunt that pinned a mob outright would
/// delete the decision it exists to create, and holding a mob would stop being something a
/// Warden has to keep doing.
/// </remarks>
public sealed class TauntEffect : IAbilityEffect, IThreatEffect
{
    /// <summary>Default lead: a quarter of the target's health bar.</summary>
    /// <remarks>
    /// Chosen so a taunt buys a window measured in someone else's damage rather than in seconds.
    /// Against anything worth taunting, a quarter of its health is several exchanges - long
    /// enough to matter, short enough that a caster who keeps going does get it back.
    /// </remarks>
    public const decimal DefaultLeadFraction = 0.25m;

    public string EffectKey => "control.taunt";

    /// <summary>
    /// Harmful, though it deals no damage. This is what decides that a bare <c>taunt</c> falls
    /// back to what the caster is fighting rather than to the caster themselves - which for this
    /// ability would be an ability that shouts at nobody.
    /// </summary>
    public bool IsHarmful => true;

    public decimal LeadFraction(Dictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        if (parameters.TryGetValue("leadFraction", out var raw) &&
            decimal.TryParse(raw, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var value) &&
            value > 0m)
        {
            // Clamped, so a misplaced decimal point is a long taunt rather than a permanent one.
            return Math.Min(value, 2m);
        }

        return DefaultLeadFraction;
    }

    /// <summary>
    /// Says what the lead is worth in the terms it is measured in - a share of the target's health
    /// bar, rather than a number of threat points nothing on screen ever shows.
    /// </summary>
    public string Describe(
        Dictionary<string, string> parameters,
        TargetingType targeting,
        int casterLevel)
    {
        var lead = LeadFraction(parameters);

        return $"makes {AbilityAudience.Whom(targeting, IsHarmful)} fight you, by a lead worth " +
               $"{AbilityAudience.Share(lead)}% of its health";
    }

    /// <summary>
    /// Deliberately does nothing. The threat change needs the room's combat, which only
    /// <c>AbilitySystem</c> can reach - see <see cref="IThreatEffect"/>.
    /// </summary>
    public void Apply(
        object caster,
        object? target,
        Dictionary<string, string> parameters,
        IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(caster);
    }
}
