using DikuWeb.Domain.Characters;

namespace DikuWeb.Domain.Abilities;

/// <summary>
/// What a level-up just granted, said out loud.
/// </summary>
/// <remarks>
/// <b>Nothing else tells the player.</b> An ability appearing in the roster is a change to a panel,
/// and a panel that gains a row silently is a feature nobody finds — which is exactly what the
/// cooldown bar's rewrite makes true, since it now shows only what is cooling and never lists what
/// a character can do. The transcript is where the game speaks, so this is where a new ability has
/// to arrive.
///
/// <b>Every intermediate level, not just the one landed on.</b> A single XP award can carry a
/// character across several levels at once — <see cref="CharacterProgression.TryLevelUp"/> jumps
/// straight to the level the total buys — so announcing only the final level would silently eat
/// everything unlocked on the way. That is the whole reason this takes a range rather than a level.
///
/// <b>Both halves of the split.</b> Castable abilities come from the table, passives from
/// <see cref="AbilityProgression.GetPassivesForPath"/>; see that class for why they live apart. A
/// player does not care about that distinction, so the two are interleaved by unlock level here and
/// read as one list.
/// </remarks>
public static class LevelUpUnlocks
{
    /// <summary>
    /// One line per thing learned crossing from <paramref name="fromLevel"/> to
    /// <paramref name="toLevel"/>, in unlock order. Empty when the levels granted nothing.
    /// </summary>
    /// <param name="abilities">
    /// The loaded ability table. An empty sequence yields passives only, which is a host that
    /// never loaded abilities rather than a Path that has none.
    /// </param>
    public static IReadOnlyList<string> Announce(
        IEnumerable<Ability> abilities,
        CharacterPath path,
        int fromLevel,
        int toLevel)
    {
        ArgumentNullException.ThrowIfNull(abilities);

        if (toLevel <= fromLevel)
        {
            return [];
        }

        // Materialised once: the two passes below both enumerate it, and the caller's sequence is
        // usually a cache's values rather than a list.
        var table = abilities as IReadOnlyList<Ability> ?? [.. abilities];

        var learned = new List<(int Level, string Line)>();

        foreach (var ability in table)
        {
            if (ability.Path != path || ability.UnlockLevel <= fromLevel || ability.UnlockLevel > toLevel)
            {
                continue;
            }

            var kind = AbilityKinds.NameOf(AbilityKinds.Of(ability));
            learned.Add((
                ability.UnlockLevel,
                $"Level {ability.UnlockLevel} — {ability.Name}, a new {kind}. Type '{AbilityKinds.VerbFor(ability)}'."));
        }

        foreach (var (unlockLevel, passiveKey) in AbilityProgression.GetPassivesForPath(path))
        {
            if (unlockLevel <= fromLevel || unlockLevel > toLevel)
            {
                continue;
            }

            // No verb: a passive is not something you type, and offering one would put a word in
            // front of the parser that can never resolve. What it does is the whole message.
            learned.Add((
                unlockLevel,
                $"Level {unlockLevel} — {PassiveKeys.NameOf(passiveKey)}. {PassiveKeys.DescriptionOf(passiveKey)}"));
        }

        if (learned.Count == 0)
        {
            return [];
        }

        // Ordered by level first so a multi-level jump reads as the climb it was, then by the line
        // itself so two unlocks at the same level have a stable order rather than the table's.
        learned.Sort((left, right) =>
            left.Level != right.Level
                ? left.Level.CompareTo(right.Level)
                : string.CompareOrdinal(left.Line, right.Line));

        List<string> lines = [.. learned.Select(entry => entry.Line)];

        // Said once however many were learned, because the roster is where the costs and cooldowns
        // are and this message deliberately carries neither.
        lines.Add("Type 'abilities' to see everything you know.");

        return lines;
    }
}
