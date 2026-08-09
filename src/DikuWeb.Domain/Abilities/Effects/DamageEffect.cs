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
    public string EffectKey => "damage.physical";

    public bool IsHarmful => true;

    public void Apply(object caster, object? target, Dictionary<string, string> parameters, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(target);

        // Get scaling factor from parameters (default 1.0 = no scaling)
        var scalingFactor = parameters.TryGetValue("scalingFactor", out var scaleStr) && double.TryParse(scaleStr, out var scale)
            ? scale
            : 1.0;

        // Get minimum damage (default 1)
        var minDamage = parameters.TryGetValue("minDamage", out var minStr) && int.TryParse(minStr, out var min)
            ? min
            : 1;

        // Base damage calculation from caster's stats (mocked for now)
        // In real implementation, this would use DamageCalculator.StatsFrom
        var baseDamage = 10;

        // Apply scaling
        var scaledDamage = (int)(baseDamage * scalingFactor);
        var finalDamage = Math.Max(minDamage, scaledDamage);

        // Apply variance (±20%)
        var variance = (int)(finalDamage * 0.2);
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
