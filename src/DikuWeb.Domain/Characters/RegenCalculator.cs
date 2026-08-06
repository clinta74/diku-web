namespace DikuWeb.Domain.Characters;

/// <summary>
/// Pure function: calculates vital regeneration based on rest state and attributes.
/// Scales with Vitality modifier to reward defensive gearing.
/// </summary>
public static class RegenCalculator
{
    /// <summary>
    /// Base regen percentage per vital per state, per tick. All vitals regen at the same rate
    /// for their state; different vitals' max values cause the absolute amounts to differ.
    /// </summary>
    private static readonly Dictionary<CharacterRestState, double> BaseRegenPercent = new()
    {
        { CharacterRestState.Sleep, 0.15 },   // 15% of max per tick
        { CharacterRestState.Rest, 0.08 },    // 8% of max per tick
        { CharacterRestState.Stand, 0.02 },   // 2% of max per tick
    };

    /// <summary>
    /// Calculate how much of each vital regenerates in a single 60-second tick.
    /// Amount is always at least 1 per vital, floored after applying modifiers.
    /// </summary>
    /// <remarks>
    /// Vitality modifier adds percentage points to the base regen rate. This ties recovery
    /// speed directly to character gearing and creates a tangible reward for stacking
    /// defensive attributes. A character with +3 Vitality modifier effectively gets 3% bonus
    /// regen across all states.
    /// </remarks>
    public static (int health, int focus, int stamina) Calculate(
        CharacterRestState state,
        Vitals vitals,
        int vitalityModifier)
    {
        var basePercent = BaseRegenPercent[state];
        var modifierBonus = vitalityModifier * 0.01; // Each modifier point adds 1%
        var effectivePercent = basePercent + modifierBonus;

        var health = Math.Max(1, (int)Math.Floor(vitals.HealthMax * effectivePercent));
        var focus = Math.Max(1, (int)Math.Floor(vitals.FocusMax * effectivePercent));
        var stamina = Math.Max(1, (int)Math.Floor(vitals.StaminaMax * effectivePercent));

        return (health, focus, stamina);
    }

    /// <summary>
    /// Apply regen to a character's vitals in place, capping to max.
    /// Returns true if any vital changed, false if already at max.
    /// </summary>
    public static bool ApplyRegen(
        CharacterRestState state,
        Vitals vitals,
        int vitalityModifier)
    {
        var (healthRegen, focusRegen, staminaRegen) = Calculate(state, vitals, vitalityModifier);

        var beforeHealth = vitals.Health;
        var beforeFocus = vitals.Focus;
        var beforeStamina = vitals.Stamina;

        vitals.Health = Math.Min(vitals.Health + healthRegen, vitals.HealthMax);
        vitals.Focus = Math.Min(vitals.Focus + focusRegen, vitals.FocusMax);
        vitals.Stamina = Math.Min(vitals.Stamina + staminaRegen, vitals.StaminaMax);

        return vitals.Health != beforeHealth || vitals.Focus != beforeFocus || vitals.Stamina != beforeStamina;
    }
}
