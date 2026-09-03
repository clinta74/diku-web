using Muwbta.Domain.Characters;
using Muwbta.Domain.Inhabitants;
using Muwbta.Domain.Randomness;

namespace Muwbta.Domain.Abilities.Effects;

/// <summary>
/// Healing effect. Restores health to a target (character or self).
/// Respects max health cap.
/// </summary>
/// <remarks>
/// <b>Two ways to say how much, and they answer different design questions.</b> <c>baseHeal</c> is a
/// flat number - "restores 70 health" - and is what nearly every heal in the game authors.
/// <c>healPercent</c> is a share of the target's own maximum, for a heal whose whole idea is
/// proportional: a second wind is not worth a fixed 18 points, it is worth *getting back on your
/// feet*, and that is a different quantity at level 13 and at level 50.
///
/// The flat form is not deprecated and should stay the default. A share-of-maximum heal is
/// self-tuning, which sounds like an unmixed good and is not: it also cannot be balanced
/// independently per tier, and it inherits every maximum-health buff in the game. Reach for it when
/// the ability's *intent* is proportional, and leave it alone otherwise.
/// </remarks>
public sealed class HealEffect : IAbilityEffect
{
    /// <summary>What a heal restores when the ability does not say.</summary>
    public const int DefaultBaseHeal = 15;

    /// <summary>The share either side of the middle a heal can land, as a fraction.</summary>
    public const double VarianceShare = 0.1;

    public string EffectKey => "heal.restore";

    public bool IsHarmful => false;

    /// <summary>
    /// The share of a target's maximum health this restores, or zero when it heals a flat amount.
    /// </summary>
    /// <remarks>
    /// Authored in whole percentage points - <c>healPercent: 50</c> - matching how <c>mitigation</c>
    /// is written on a guard effect, so a builder never has to remember which fields are fractions.
    /// Clamped to a sane range: a negative share would be a heal that wounds, and nothing is served
    /// by letting one exceed a full bar when the result is capped at maximum anyway.
    /// </remarks>
    public static double PercentOf(Dictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        return parameters.TryGetValue("healPercent", out var raw) && double.TryParse(raw, out var value)
            ? Math.Clamp(value, 0, 100) / 100.0
            : 0;
    }

    /// <summary>
    /// What this restores before variance, for a target with this maximum health.
    /// </summary>
    /// <remarks>
    /// <paramref name="targetHealthMax"/> is only read by the proportional form, and is zero
    /// wherever the caller has no target in hand - the ability listing, for one, which describes a
    /// heal before anybody has been aimed at. A proportional heal describes itself as a percentage
    /// in that case rather than inventing a number.
    /// </remarks>
    public static int Middle(Dictionary<string, string> parameters, int targetHealthMax = 0)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var percent = PercentOf(parameters);

        if (percent > 0 && targetHealthMax > 0)
        {
            return Math.Max(1, (int)Math.Round(targetHealthMax * percent, MidpointRounding.AwayFromZero));
        }

        // Get base heal amount from parameters (default 15)
        return parameters.TryGetValue("baseHeal", out var baseStr) && int.TryParse(baseStr, out var base_val)
            ? base_val
            : DefaultBaseHeal;
    }

    /// <summary>How far either side of <see cref="Middle"/> a heal can fall.</summary>
    public static int Variance(int middle) => (int)(middle * VarianceShare);

    public string Describe(
        Dictionary<string, string> parameters,
        TargetingType targeting,
        int casterLevel)
    {
        var percent = PercentOf(parameters);

        // Described as the share it is, because that is the true and complete statement of what a
        // proportional heal does - and because the listing has no target to take a maximum from.
        // Quoting the caster's own maximum would be right only when they heal themselves.
        if (percent > 0)
        {
            return $"restores {percent:P0} of maximum health to " +
                   AbilityAudience.Whom(targeting, IsHarmful);
        }

        var middle = Middle(parameters);
        var spread = Variance(middle);

        return $"restores {AbilityAudience.Amount(middle - spread, middle + spread)} health to " +
               AbilityAudience.Whom(targeting, IsHarmful);
    }

    public void Apply(object caster, object? target, Dictionary<string, string> parameters, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(random);

        var vitals = target switch
        {
            Character character => character.Vitals,
            Mob mob => mob.Vitals,
            _ => null,
        };

        if (vitals is null)
        {
            return;
        }

        // The *target's* maximum, not the caster's: a Hallow healing a Warden restores a share of
        // what the Warden can hold, which is the only reading that makes a proportional heal mean
        // the same thing to whoever receives it.
        var baseHeal = Middle(parameters, vitals.HealthMax);

        // Apply variance (±10%)
        var variance = Variance(baseHeal);
        var variance_amount = random.Next(-variance, variance + 1);
        var healing = baseHeal + variance_amount;

        vitals.Health = Math.Min(vitals.HealthMax, vitals.Health + healing);
    }
}
