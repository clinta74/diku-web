namespace DikuWeb.Domain.Characters;

/// <summary>
/// Dividing a kill's experience and gold between the people who were there (PLAN.md §5.3).
/// </summary>
public static class RewardShare
{
    /// <summary>
    /// Splits a reward into <paramref name="shares"/> parts, largest first.
    /// </summary>
    /// <remarks>
    /// <b>An even split, with the remainder going to the first share.</b> Weighting it by level or
    /// by damage dealt was the alternative, and both punish the thing grouping is for: a level 20
    /// helping a level 5 through a zone would earn most of the reward for the help, and splitting
    /// by damage makes a Warden's job pay less than an Adept's for the same fight.
    ///
    /// There is deliberately <b>no group bonus</b>. A party of four killing one mob earns exactly
    /// what one player killing it earns, so grouping is currently a social choice rather than an
    /// efficient one - if that turns out to be the wrong call it is one number here, but inventing
    /// a multiplier before anyone has played in a group would be balancing against a guess.
    ///
    /// The remainder goes to the first share rather than being dropped, so a reward never shrinks
    /// by being divided: 7 experience between 2 is 4 and 3, not 3 and 3.
    /// </remarks>
    public static IReadOnlyList<long> Split(long total, int shares)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(shares, 1);

        if (shares == 1)
        {
            return [total];
        }

        // Negative totals are not a thing any caller produces, but dividing one would hand the
        // first share the largest debt, which is the opposite of what "largest first" means.
        if (total <= 0)
        {
            return [.. Enumerable.Repeat(0L, shares)];
        }

        var each = total / shares;
        var remainder = total - (each * shares);

        var split = new long[shares];
        for (var i = 0; i < shares; i++)
        {
            split[i] = each;
        }

        split[0] += remainder;
        return split;
    }
}
