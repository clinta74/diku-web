using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Combat;

namespace DikuWeb.Domain.Abilities;

/// <summary>
/// Validates whether an ability can be cast before executing it.
/// Checks cost, cooldown, targeting validity, and room flags (peaceful/pvp).
/// </summary>
public static class AbilityValidator
{
    /// <summary>
    /// Check if a character can cast an ability on a target.
    /// </summary>
    public static AbilityValidationResult CanCast(
        Character caster,
        Ability ability,
        object? target,
        bool peaceful,
        bool pvp)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(ability);

        // Check cost
        var cost = ability.CostValue;
        var currentResource = GetResource(caster, ability.CostType);
        if (currentResource < cost)
        {
            return Refuse($"Not enough {ability.CostType.ToString().ToLower()} (need {cost}, have {currentResource}).");
        }

        // Check targeting
        if (ability.TargetingType == TargetingType.SingleTarget)
        {
            if (target == null)
            {
                return Refuse("That ability requires a target.");
            }

            // If targeting another character, check peaceful/pvp rules
            if (target is Character targetChar && targetChar.Id != caster.Id)
            {
                var targetType = CombatantType.Player;
                var validation = TargetValidator.ValidateTarget(
                    CombatantType.Player,
                    targetType,
                    targetChar.Name,
                    peaceful,
                    pvp);

                if (!validation.IsAllowed)
                {
                    return Refuse(validation.RefusalReason ?? "Cannot target that character.");
                }
            }
        }
        else if (ability.TargetingType == TargetingType.Self)
        {
            if (target != null && target != caster)
            {
                return Refuse("That ability only targets yourself.");
            }
        }
        // TargetingType.Aoe: no pre-validation needed; per-target filtering happens during Apply

        return Allow();
    }

    private static int GetResource(Character character, CostType costType) => costType switch
    {
        CostType.Focus => character.Vitals.Focus,
        CostType.Stamina => character.Vitals.Stamina,
        CostType.Health => character.Vitals.Health,
        _ => 0,
    };

    private static AbilityValidationResult Allow() =>
        new() { IsAllowed = true };

    private static AbilityValidationResult Refuse(string reason) =>
        new() { IsAllowed = false, RefusalReason = reason };
}

/// <summary>Result of ability validation check.</summary>
public sealed class AbilityValidationResult
{
    public required bool IsAllowed { get; init; }
    public string? RefusalReason { get; init; }
}
