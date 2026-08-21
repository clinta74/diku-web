using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Randomness;

namespace DikuWeb.Domain.Abilities.Effects;

/// <summary>
/// Healing effect. Restores health to a target (character or self).
/// Respects max health cap.
/// </summary>
public sealed class HealEffect : IAbilityEffect
{
    /// <summary>What a heal restores when the ability does not say.</summary>
    public const int DefaultBaseHeal = 15;

    /// <summary>The share either side of the middle a heal can land, as a fraction.</summary>
    public const double VarianceShare = 0.1;

    public string EffectKey => "heal.restore";

    public bool IsHarmful => false;

    /// <summary>What this restores before variance. Shared with <see cref="Describe"/>.</summary>
    public static int Middle(Dictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

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

        var baseHeal = Middle(parameters);

        // Apply variance (±10%)
        var variance = Variance(baseHeal);
        var variance_amount = random.Next(-variance, variance + 1);
        var healing = baseHeal + variance_amount;

        // Apply healing to target
        if (target is Character targetChar)
        {
            targetChar.Vitals.Health = Math.Min(targetChar.Vitals.HealthMax, targetChar.Vitals.Health + healing);
        }
        else if (target is Mob targetMob)
        {
            targetMob.Vitals.Health = Math.Min(targetMob.Vitals.HealthMax, targetMob.Vitals.Health + healing);
        }
    }
}
