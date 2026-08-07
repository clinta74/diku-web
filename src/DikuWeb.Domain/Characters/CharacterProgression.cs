namespace DikuWeb.Domain.Characters;

/// <summary>
/// Handles character progression: leveling up, applying stat growth, and recalculating vitals.
/// Pure function taking a character's current state and returning updates if they've leveled.
/// </summary>
public static class CharacterProgression
{
    public sealed record LevelUpResult(int NewLevel, int LeveledUp, AttributeSet NewAttributes, Vitals NewVitals);

    /// <summary>
    /// Check if the character should level up based on their current XP. If so, apply growth
    /// and return the new state. Otherwise, return null.
    /// </summary>
    /// <remarks>
    /// If a character jumps multiple levels (e.g., a large XP reward), this function processes
    /// only one level. Callers should loop until no more levels are gained.
    /// </remarks>
    public static LevelUpResult? TryLevelUp(int currentLevel, long currentXp, AttributeSet attributes, CharacterPath path, Vitals vitals)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(currentLevel, XpProgression.MinLevel);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(currentLevel, XpProgression.MaxLevel);

        var newLevel = XpProgression.LevelForXp(currentXp);

        // Not enough XP for the next level yet
        if (newLevel <= currentLevel)
        {
            return null;
        }

        // Cap at max level
        newLevel = Math.Min(newLevel, XpProgression.MaxLevel);

        // Apply Path-based stat growth for each level gained
        var newAttributes = attributes;
        var growth = PathGrowth.For(path);
        for (var i = 0; i < newLevel - currentLevel; i++)
        {
            growth.ApplyTo(ref newAttributes);
        }

        // Recalculate vital maxima based on new attributes
        var newVitals = new Vitals
        {
            Health = vitals.Health,
            Focus = vitals.Focus,
            Stamina = vitals.Stamina,
        };
        VitalCalculator.RecalculateMaxima(path, newLevel, newAttributes, newVitals);

        // Cap current vitals to new maxima (in case max dropped somehow, though it shouldn't)
        newVitals.Health = Math.Min(newVitals.Health, newVitals.HealthMax);
        newVitals.Focus = Math.Min(newVitals.Focus, newVitals.FocusMax);
        newVitals.Stamina = Math.Min(newVitals.Stamina, newVitals.StaminaMax);

        return new LevelUpResult(
            NewLevel: newLevel,
            LeveledUp: newLevel - currentLevel,
            NewAttributes: newAttributes,
            NewVitals: newVitals);
    }

    /// <summary>
    /// Award XP to a character and apply any level-ups. Returns the number of levels gained
    /// and the new character state, or null if no levels were gained.
    /// </summary>
    public static LevelUpResult? AwardXp(
        Character character,
        long xpAwarded)
    {
        if (xpAwarded < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(xpAwarded), "XP awarded must be non-negative.");
        }

        var newXp = character.Xp + xpAwarded;
        return TryLevelUp(character.Level, newXp, character.Attributes, character.Path, character.Vitals);
    }
}
