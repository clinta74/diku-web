using DikuWeb.Domain.Characters;

namespace DikuWeb.Domain.Abilities;

/// <summary>
/// Defines which abilities each Path unlocks at which levels.
/// Abilities are fixed at creation per Path (Q3 resolved: no respec in v1).
/// </summary>
public static class AbilityProgression
{
    /// <summary>
    /// Get all abilities a character should know based on their Path and current level.
    /// Returns (UnlockLevel, AbilityKey) tuples in order of unlock.
    /// </summary>
    public static IReadOnlyList<(int UnlockLevel, string AbilityKey)> GetAbilitiesForPath(CharacterPath path) =>
        path switch
        {
            CharacterPath.Warden => [
                (1, "warden.slash"),
                (3, "warden.bash"),
                (6, "warden.parry"),
            ],
            CharacterPath.Adept => [
                (1, "adept.bolt"),
                (3, "adept.shield"),
                (6, "adept.amplify"),
            ],
            CharacterPath.Shade => [
                (1, "shade.strike"),
                (3, "shade.evasion"),
                (6, "shade.shadowstep"),
            ],
            CharacterPath.Channeler => [
                (1, "channeler.mend"),
                (3, "channeler.guidance"),
                (6, "channeler.restore"),
            ],
            _ => [],
        };

    /// <summary>
    /// Get the abilities a character currently knows (at or below their level).
    /// </summary>
    public static IReadOnlyList<string> GetKnownAbilitiesForLevel(CharacterPath path, int level) =>
        GetAbilitiesForPath(path)
            .Where(x => x.UnlockLevel <= level)
            .Select(x => x.AbilityKey)
            .ToList();

    /// <summary>
    /// Check if a character knows a specific ability.
    /// </summary>
    public static bool Knows(CharacterPath path, int level, string abilityKey) =>
        GetKnownAbilitiesForLevel(path, level).Contains(abilityKey);
}
