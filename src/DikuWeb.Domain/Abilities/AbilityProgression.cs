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
            CharacterPath.Temper => [
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
    /// what the Path is. A Temper parries by footwork, which is later and less reliable, and is
    /// meant to be the lesser half of not being hit - evasion and simply not being there are the
    /// rest. An Adept or Hallow never parries at all.
    /// </remarks>
    public static double ParryChance(CharacterPath path, int level) =>
        KnowsPassive(path, level, PassiveKeys.Parry)
            ? path switch
            {
                CharacterPath.Warden => 0.20,
                CharacterPath.Temper => 0.12,
                _ => 0.0,
            }
            : 0.0;

    /// <summary>
    /// The level by which a dual-wielder's off hand reaches its full share of damage.
    /// </summary>
    /// <remarks>
    /// Deliberately far out. The obvious axis for this was Agility, and it does not work: Agility
    /// caps at <c>AttributeSet.MaxValue</c>, which a Temper reaches at level 6 and a Warden at 11, so
    /// the ramp would have been over before the passive had been held for long.
    /// </remarks>
    public const int OffHandMasteryLevel = 40;

    /// <summary>
    /// The share, 0.0-1.0, of its damage an off-hand weapon actually deals.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A second weapon arrives partly trained and grows into being a second weapon.</b> Before
    /// this it arrived whole: the day a Temper learned Dual Wield their off hand hit for everything
    /// the main hand did, and Ambidextrous later doubled the rate as well. The doubling at the top
    /// is intended and untouched - a Temper at <see cref="OffHandMasteryLevel"/> and beyond is
    /// exactly where it was. What changes is everything before it.
    /// </para>
    /// <para>
    /// Straight line from the level the Path learns <c>DualWield</c> to
    /// <see cref="OffHandMasteryLevel"/>, starting at half the eventual share so the passive is
    /// worth having on the day it is granted rather than being a promise about level 40. The
    /// endpoints are the Path's identity: a Temper ends at <b>all</b> of it, because two blades is
    /// what the Path is, and a Warden at <b>four fifths</b>, because the hand is also holding a
    /// shield most of the time.
    /// </para>
    /// <para>
    /// Zero for anyone who has not learned to strike with an off hand at all - an Adept may hold a
    /// blade there and it simply never swings.
    /// </para>
    /// </remarks>
    public static decimal OffHandDamageShare(CharacterPath path, int level)
    {
        if (!KnowsPassive(path, level, PassiveKeys.DualWield))
        {
            return 0m;
        }

        var full = path switch
        {
            CharacterPath.Temper => 1.00m,
            CharacterPath.Warden => 0.80m,
            _ => 0m,
        };

        if (full == 0m)
        {
            return 0m;
        }

        var unlock = GetPassivesForPath(path)
            .Where(x => string.Equals(x.PassiveKey, PassiveKeys.DualWield, StringComparison.Ordinal))
            .Select(x => x.UnlockLevel)
            .DefaultIfEmpty(1)
            .Min();

        if (level >= OffHandMasteryLevel || OffHandMasteryLevel <= unlock)
        {
            return full;
        }

        // Half at the unlock, full at mastery, straight line between.
        var progress = (decimal)(level - unlock) / (OffHandMasteryLevel - unlock);

        return (full / 2m) + (full / 2m * Math.Clamp(progress, 0m, 1m));
    }

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
