namespace DikuWeb.Domain.Characters;

/// <summary>
/// Calculates maximum vitals (Health, Focus, Stamina) from character level, Path, and attributes.
/// Vital maxima scale with level and attribute modifiers.
/// </summary>
public static class VitalCalculator
{
    /// <summary>
    /// Calculate the maximum health for a character at a given level with the given attributes.
    /// </summary>
    public static int HealthMax(CharacterPath path, int level, AttributeSet attributes)
    {
        // Base health from Path starting values
        var baseVitals = Vitals.StartingFor(path);
        var startingHealth = baseVitals.HealthMax;

        // Scale with level and Vitality modifier
        // Each level adds ~5 base health, and Vitality helps too
        var levelBonus = (level - 1) * 5;
        var vitalityBonus = attributes.VitalityModifier * 2;

        return Math.Max(1, startingHealth + levelBonus + vitalityBonus);
    }

    /// <summary>
    /// Calculate the maximum focus for a character at a given level with the given attributes.
    /// Focus (mana) scales less steeply than health but benefits from Insight.
    /// </summary>
    public static int FocusMax(CharacterPath path, int level, AttributeSet attributes)
    {
        var baseVitals = Vitals.StartingFor(path);
        var startingFocus = baseVitals.FocusMax;

        // Each level adds ~3 base focus, Insight helps
        var levelBonus = (level - 1) * 3;
        var insightBonus = attributes.InsightModifier * 2;

        return Math.Max(0, startingFocus + levelBonus + insightBonus);
    }

    /// <summary>
    /// Calculate the maximum stamina for a character at a given level with the given attributes.
    /// Stamina (for actions) scales with level and Agility.
    /// </summary>
    public static int StaminaMax(CharacterPath path, int level, AttributeSet attributes)
    {
        var baseVitals = Vitals.StartingFor(path);
        var startingStamina = baseVitals.StaminaMax;

        // Each level adds ~3 base stamina, Agility helps
        var levelBonus = (level - 1) * 3;
        var agilityBonus = attributes.AgilityModifier * 2;

        return Math.Max(0, startingStamina + levelBonus + agilityBonus);
    }

    /// <summary>
    /// Recalculate all three vital maxima and update the Vitals object.
    /// Does not change current Health/Focus/Stamina values — the caller decides what happens
    /// (fill on level-up, stay same on attribute change, etc.).
    /// </summary>
    public static void RecalculateMaxima(CharacterPath path, int level, AttributeSet attributes, Vitals vitals)
    {
        vitals.HealthMax = HealthMax(path, level, attributes);
        vitals.FocusMax = FocusMax(path, level, attributes);
        vitals.StaminaMax = StaminaMax(path, level, attributes);
    }
}
