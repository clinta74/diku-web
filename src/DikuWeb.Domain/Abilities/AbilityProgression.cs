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
    /// <remarks>
    /// Derived from <see cref="AbilityCatalogue"/> rather than listed again here. This was a
    /// hand-written table beside a hand-written seeder, and the two had drifted apart in both
    /// directions - unlocks with no ability row, and ability rows nothing ever unlocked.
    /// </remarks>
    public static IReadOnlyList<(int UnlockLevel, string AbilityKey)> GetAbilitiesForPath(CharacterPath path) =>
        [.. AbilityCatalogue.For(path).Select(e => (e.UnlockLevel, e.Key))];

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

    /// <summary>
    /// Passives a Path grants by level. Deliberately a separate list from the castable abilities:
    /// a passive has no ability row, no cost, and nothing to target, so letting one into
    /// <see cref="GetAbilitiesForPath"/> would put a key in front of <c>cast</c> that can never
    /// resolve.
    /// </summary>
    /// <remarks>
    /// Only the two martial Paths learn to fight with a second weapon. An Adept or Channeler may
    /// still put a blade in their off hand; it simply never strikes.
    /// </remarks>
    public static IReadOnlyList<(int UnlockLevel, string PassiveKey)> GetPassivesForPath(CharacterPath path) =>
        path switch
        {
            CharacterPath.Warden => [
                (5, PassiveKeys.DualWield),
                (15, PassiveKeys.Ambidextrous),
            ],
            CharacterPath.Shade => [
                (3, PassiveKeys.DualWield),
                (10, PassiveKeys.Ambidextrous),
            ],
            _ => [],
        };

    /// <summary>
    /// Get the passives a character currently has (at or below their level).
    /// </summary>
    public static IReadOnlyList<string> GetKnownPassivesForLevel(CharacterPath path, int level) =>
        GetPassivesForPath(path)
            .Where(x => x.UnlockLevel <= level)
            .Select(x => x.PassiveKey)
            .ToList();

    /// <summary>
    /// Check if a character has a specific passive.
    /// </summary>
    public static bool KnowsPassive(CharacterPath path, int level, string passiveKey) =>
        GetPassivesForPath(path).Any(x => x.UnlockLevel <= level && x.PassiveKey == passiveKey);
}
