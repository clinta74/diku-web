using DikuWeb.Domain.Characters;

namespace DikuWeb.Domain.Abilities;

/// <summary>
/// Which abilities and passives a Path has by a given level.
/// Abilities are fixed at creation per Path (Q3 resolved: no respec in v1).
/// </summary>
/// <remarks>
/// <b>This class answers in two halves, from two different sources, and the split is deliberate.</b>
///
/// *Abilities* are rows in the <c>abilities</c> table, so the methods that answer about them take
/// the ability set as an argument rather than reading a list here. They used to read
/// <see cref="AbilityCatalogue"/>, which was correct while the catalogue was authoritative and
/// became a lie the moment a builder could edit a row: a Path would have gone on being granted
/// whatever the code said, while casting resolved against whatever the table said.
///
/// *Passives* — parry, dual-wield, ambidextrous — stay here in code, because a passive has no row
/// in that table. It has no cost, no target, and no effect to execute; it is a threshold the
/// combat system reads directly. Letting one into the ability list would put a key in front of
/// <c>cast</c> that can never resolve.
///
/// That is two sources of truth for "what does this Path get at level N", which is the exact shape
/// of the drift 5.1e removed — so it is held together by a test rather than by intention: no
/// passive key may collide with an ability key, and every Path's two lists are checked for gaps
/// together rather than separately.
/// </remarks>
public static class AbilityProgression
{
    /// <summary>
    /// Every ability <paramref name="path"/> learns, in unlock order, drawn from
    /// <paramref name="abilities"/> — the loaded table, not a list in code.
    /// </summary>
    /// <remarks>
    /// Ordered here rather than trusted from the caller: the cache is a dictionary and a level-up
    /// message that listed abilities out of order would read as a bug in the level table.
    /// </remarks>
    public static IReadOnlyList<(int UnlockLevel, string AbilityKey)> GetAbilitiesForPath(
        IEnumerable<Ability> abilities,
        CharacterPath path)
    {
        ArgumentNullException.ThrowIfNull(abilities);

        return
        [
            .. abilities
                .Where(a => a.Path == path)
                .OrderBy(a => a.UnlockLevel)
                .ThenBy(a => a.Key, StringComparer.Ordinal)
                .Select(a => (a.UnlockLevel, a.Key)),
        ];
    }

    /// <summary>
    /// The abilities a character currently knows (unlocked at or below their level).
    /// </summary>
    public static IReadOnlyList<string> GetKnownAbilitiesForLevel(
        IEnumerable<Ability> abilities,
        CharacterPath path,
        int level) =>
        [.. GetAbilitiesForPath(abilities, path)
            .Where(x => x.UnlockLevel <= level)
            .Select(x => x.AbilityKey)];

    /// <summary>
    /// Whether a character knows a specific ability.
    /// </summary>
    public static bool Knows(
        IEnumerable<Ability> abilities,
        CharacterPath path,
        int level,
        string abilityKey) =>
        GetKnownAbilitiesForLevel(abilities, path, level).Contains(abilityKey, StringComparer.Ordinal);

    /// <summary>
    /// Passives a Path grants by level. Deliberately a separate list from the castable abilities:
    /// a passive has no ability row, no cost, and nothing to target, so letting one into
    /// <see cref="GetAbilitiesForPath"/> would put a key in front of <c>cast</c> that can never
    /// resolve.
    /// </summary>
    /// <remarks>
    /// Only the two martial Paths learn to fight with a second weapon. An Adept or Hallow may
    /// still put a blade in their off hand; it simply never strikes.
    /// </remarks>
    public static IReadOnlyList<(int UnlockLevel, string PassiveKey)> GetPassivesForPath(CharacterPath path) =>
        path switch
        {
            CharacterPath.Warden => [
                (4, PassiveKeys.Parry),
                (5, PassiveKeys.DualWield),
                (15, PassiveKeys.Ambidextrous),
            ],
            CharacterPath.Shade => [
                (3, PassiveKeys.DualWield),
                (8, PassiveKeys.Parry),
                (10, PassiveKeys.Ambidextrous),
            ],
            _ => [],
        };

    /// <summary>
    /// The chance, 0.0-1.0, that this character turns aside a blow that would have landed.
    /// Zero for anyone who has not learned to parry.
    /// </summary>
    /// <remarks>
    /// The Warden parries more often and earlier: a shield and a braced stance are the whole of
    /// what the Path is. A Shade parries by footwork, which is later and less reliable, and is
    /// meant to be the lesser half of not being hit - evasion and simply not being there are the
    /// rest. An Adept or Hallow never parries at all.
    /// </remarks>
    public static double ParryChance(CharacterPath path, int level) =>
        KnowsPassive(path, level, PassiveKeys.Parry)
            ? path switch
            {
                CharacterPath.Warden => 0.20,
                CharacterPath.Shade => 0.12,
                _ => 0.0,
            }
            : 0.0;

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
