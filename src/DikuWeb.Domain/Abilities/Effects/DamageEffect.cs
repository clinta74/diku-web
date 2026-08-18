using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Randomness;

namespace DikuWeb.Domain.Abilities.Effects;

/// <summary>
/// Physical or magical damage effect. Applies to a single target (character or mob).
/// Scaling factor and variance come from ability parameters.
/// </summary>
public sealed class DamageEffect : IAbilityEffect
{
    /// <summary>
    /// What <c>scalingFactor</c> currently scales.
    /// </summary>
    /// <remarks>
    /// <b>A stub, and named rather than inline so that it is one.</b> This is meant to come from
    /// the caster - level and casting attribute, the way <c>EquipmentResolver</c> builds a weapon's
    /// swing - and until it does, every ability in the game deals the same damage at level 50 as at
    /// the level it was learned. <see cref="Describe"/> reports what this actually produces, so the
    /// flatness is now visible in play instead of only in this comment.
    /// </remarks>
    public const int UnscaledBaseDamage = 10;

    /// <summary>The share either side of the middle a blow can land, as a fraction.</summary>
    public const double VarianceShare = 0.2;

    public string EffectKey => "damage.physical";

    public bool IsHarmful => true;

    /// <summary>
    /// The damage this lands for, before variance - which is what the dials add up to.
    /// </summary>
    /// <remarks>
    /// Shared with <see cref="Describe"/> rather than repeated there, so the number a player is
    /// shown is arrived at by the code that deals it. When the base stops being a constant this
    /// moves with it and the description follows.
    /// </remarks>
    public static int Middle(Dictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        // Get scaling factor from parameters (default 1.0 = no scaling)
        var scalingFactor = parameters.TryGetValue("scalingFactor", out var scaleStr) && double.TryParse(scaleStr, out var scale)
            ? scale
            : 1.0;

        // Get minimum damage (default 1)
        var minDamage = parameters.TryGetValue("minDamage", out var minStr) && int.TryParse(minStr, out var min)
            ? min
            : 1;

        var scaledDamage = (int)(UnscaledBaseDamage * scalingFactor);

        return Math.Max(minDamage, scaledDamage);
    }

    /// <summary>How far either side of <see cref="Middle"/> a roll can fall.</summary>
    public static int Variance(int middle) => (int)(middle * VarianceShare);

    public string Describe(Dictionary<string, string> parameters, TargetingType targeting)
    {
        var middle = Middle(parameters);
        var spread = Variance(middle);

        return $"deals {AbilityAudience.Amount(middle - spread, middle + spread)} damage to " +
               AbilityAudience.Whom(targeting, IsHarmful);
    }

    public void Apply(object caster, object? target, Dictionary<string, string> parameters, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(random);

        var finalDamage = Middle(parameters);

        // Apply variance (±20%)
        var variance = Variance(finalDamage);
        var variance_amount = random.Next(-variance, variance + 1);
        var damage = finalDamage + variance_amount;

        // Apply damage to target
        if (target is Character targetChar)
        {
            targetChar.Vitals.Health = Math.Max(0, targetChar.Vitals.Health - damage);
        }
        else if (target is Mob targetMob)
        {
            targetMob.Vitals.Health = Math.Max(0, targetMob.Vitals.Health - damage);
        }
    }
}
