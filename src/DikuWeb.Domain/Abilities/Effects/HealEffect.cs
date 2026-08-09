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
    public string EffectKey => "heal.restore";

    public bool IsHarmful => false;

    public void Apply(object caster, object? target, Dictionary<string, string> parameters, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(target);

        // Get base heal amount from parameters (default 15)
        var baseHeal = parameters.TryGetValue("baseHeal", out var baseStr) && int.TryParse(baseStr, out var base_val)
            ? base_val
            : 15;

        // Apply variance (±10%)
        var variance = (int)(baseHeal * 0.1);
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
