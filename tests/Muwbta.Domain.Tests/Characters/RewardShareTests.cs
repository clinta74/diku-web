using Muwbta.Domain.Characters;

namespace Muwbta.Domain.Tests.Characters;

/// <summary>
/// Dividing a kill between the people who were there (PLAN.md §5.3).
/// </summary>
public sealed class RewardShareTests
{
    [Fact]
    public void One_share_is_the_whole_reward()
    {
        // The common case: a solo kill goes through the same code as a grouped one, so there is
        // no second path for the split to disagree with.
        Assert.Equal([100L], RewardShare.Split(100, 1));
    }

    [Fact]
    public void An_even_reward_divides_evenly()
    {
        Assert.Equal([25L, 25L, 25L, 25L], RewardShare.Split(100, 4));
    }

    [Fact]
    public void The_remainder_goes_to_the_first_share()
    {
        // A reward must never shrink by being divided: 7 between 2 is 4 and 3, not 3 and 3.
        Assert.Equal([4L, 3L], RewardShare.Split(7, 2));
        Assert.Equal([3L, 2L, 2L], RewardShare.Split(7, 3));
    }

    [Fact]
    public void The_total_survives_the_split()
    {
        for (var total = 0; total < 40; total++)
        {
            for (var shares = 1; shares <= Engine_max_party; shares++)
            {
                Assert.Equal(total, RewardShare.Split(total, shares).Sum());
            }
        }
    }

    [Fact]
    public void A_reward_too_small_to_divide_still_pays_the_killer()
    {
        // Three experience between six is not worth a sixth each. What it is worth goes to the
        // person who landed the blow rather than evaporating.
        Assert.Equal([3L, 0L, 0L, 0L, 0L, 0L], RewardShare.Split(3, 6));
    }

    [Fact]
    public void Nothing_divides_into_nothing()
    {
        Assert.Equal([0L, 0L], RewardShare.Split(0, 2));
    }

    [Fact]
    public void Fewer_than_one_share_is_a_programming_error()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RewardShare.Split(10, 0));
    }

    /// <summary>Mirrors <c>Party.MaxMembers</c>; Domain does not reference the Engine.</summary>
    private const int Engine_max_party = 6;
}
