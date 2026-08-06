namespace DikuWeb.Domain.Characters;

/// <summary>
/// XP progression curve for levels 1–50 (PLAN.md §4.7).
/// Formula: cumulative, roughly quadratic so later levels feel more rewarding.
/// </summary>
public static class XpProgression
{
    public const int MinLevel = 1;
    public const int MaxLevel = 50;

    /// <summary>
    /// Total XP required to reach a given level (not the delta from previous level).
    /// Level 1 starts at 0 XP. Level 2 requires 1000 XP total. Level 3 requires 3000 XP total, etc.
    /// </summary>
    public static long XpForLevel(int level)
    {
        if (level < MinLevel || level > MaxLevel)
            throw new ArgumentOutOfRangeException(nameof(level), $"Level must be {MinLevel}–{MaxLevel}.");

        if (level == MinLevel)
            return 0;

        // Quadratic curve: 1000 * level * (level - 1) / 2
        // This gives: L2=1000, L3=3000, L4=6000, L5=10000, ... L50=1,225,000
        return 1000L * level * (level - 1) / 2;
    }

    /// <summary>
    /// Determine the current level based on total accumulated XP.
    /// </summary>
    public static int LevelForXp(long xp)
    {
        if (xp < 0)
            return MinLevel;

        // Binary search to find the highest level where XpForLevel(level) <= xp
        int level = MaxLevel;
        for (int i = MaxLevel; i >= MinLevel; i--)
        {
            if (XpForLevel(i) <= xp)
            {
                level = i;
                break;
            }
        }

        return level;
    }
}
