namespace DikuWeb.Domain.Characters;

/// <summary>
/// Whether a kill was worth anything to the person who made it, and to the people standing next to
/// them (PLAN.md §5.3).
/// </summary>
/// <remarks>
/// <b>One rule, two uses.</b> A mob far enough below you is not an accomplishment, and neither is
/// standing beside someone who is clearing content you could not survive. Both questions are "is
/// this within reach of that level", so both are answered by <see cref="Floor"/> rather than by two
/// separately tuned numbers that would drift apart the first time either was adjusted.
///
/// <b>The taper is a slope, not a cliff.</b> The point is keeping effort and reward in line, and a
/// hard cutoff does the opposite at the boundary — one level of difference either side of it would
/// be the difference between full value and nothing, which teaches players to count levels rather
/// than to pick fights. Full value at or above your level, nothing below the floor, and a straight
/// line between.
///
/// <b>Applied after zone multipliers, which is the whole point.</b> A zone can scale a reward
/// (§4.4) and must not be able to resurrect a worthless one: ten times zero is zero. Were the order
/// reversed, an eight-times-experience starter zone would be the best farm in the game for a level
/// 50, which is precisely the behaviour this exists to stop.
/// </remarks>
public static class XpRelevance
{
    /// <summary>
    /// The lowest level still within reach of <paramref name="level"/> — the last level that earns
    /// anything at all.
    /// </summary>
    /// <remarks>
    /// Half your level, so the window widens as you climb: a level 10 is done with everything below
    /// 5, a level 50 with everything below 25. That is the right shape, because the gap between
    /// level 1 and 5 is most of a new character's life and the gap between 45 and 50 is a weekend.
    ///
    /// <b>The cap at 30 does nothing today</b>, and is here on purpose rather than by oversight:
    /// it only binds above level 60 and <see cref="XpProgression.MaxLevel"/> is 50. It is the
    /// statement that the window stops widening eventually, kept so that raising the cap is one
    /// number rather than a rediscovery of this question.
    /// </remarks>
    public static int Floor(int level) => Math.Min(level / 2, 30);

    /// <summary>
    /// What fraction of a kill's experience a character of <paramref name="killerLevel"/> earns
    /// from a mob of <paramref name="mobLevel"/>. Between 0 and 1.
    /// </summary>
    public static double Fraction(int killerLevel, int mobLevel)
    {
        // At or above your level is a fair fight or better, and nothing here reduces it. There is
        // deliberately no bonus for punching up either: that would be a separate decision about
        // what the game rewards, and inventing it here would hide it.
        if (mobLevel >= killerLevel)
        {
            return 1.0;
        }

        var floor = Floor(killerLevel);

        // Strictly below the floor is beneath you. The floor itself still pays, which is what makes
        // "a level 50 grouped with a level 25" work in ShareOf - 25 is the last level that counts,
        // not the first that does not.
        if (mobLevel < floor)
        {
            return 0.0;
        }

        // +1 on both sides so the floor earns the smallest non-zero slice rather than nothing,
        // which would make the floor mean two different things in the two places it is used.
        return (double)(mobLevel - floor + 1) / (killerLevel - floor + 1);
    }

    /// <summary>
    /// Applies <see cref="Fraction"/> to an already zone-scaled experience award.
    /// </summary>
    /// <remarks>
    /// <b>A kill that counts never rounds down to nothing.</b> Otherwise a small reward at the
    /// bottom of the taper is indistinguishable from a trivial one, and "you got no experience"
    /// would mean both "that was beneath you" and "that was worth 0.4 experience" — the first is
    /// information and the second is a bug report.
    /// </remarks>
    public static long ShareOf(long resolvedXp, int killerLevel, int mobLevel)
    {
        if (resolvedXp <= 0)
        {
            return 0;
        }

        var fraction = Fraction(killerLevel, mobLevel);
        if (fraction <= 0)
        {
            return 0;
        }

        return Math.Max(1, (long)Math.Round(resolvedXp * fraction, MidpointRounding.AwayFromZero));
    }

    /// <summary>
    /// Whether a party member earns any experience from a kill, given the highest level present.
    /// </summary>
    /// <remarks>
    /// The same window, pointed at a person instead of a mob: a level 24 earns nothing from a level
    /// 50's kills, and a level 25 earns their share. It closes the obvious version of power
    /// levelling without forbidding the useful one — carrying a friend who is close behind you is
    /// still exactly as effective as it was.
    ///
    /// <b>Highest level <em>present</em>, not highest on the roster.</b> A party member who is not
    /// at the fight already shares nothing (§5.3), and letting them set the floor anyway would mean
    /// a level 50 standing in town could switch off their friends' experience across the map.
    /// </remarks>
    public static bool SharesExperience(int memberLevel, int highestLevelPresent) =>
        memberLevel >= Floor(highestLevelPresent);
}
