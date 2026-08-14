using DikuWeb.Domain.Characters;

namespace DikuWeb.Domain.Tests.Characters;

/// <summary>
/// The window that decides whether a kill was worth anything (PLAN.md §5.3).
/// </summary>
public sealed class XpRelevanceTests
{
    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(10, 5)]
    [InlineData(25, 12)]
    [InlineData(50, 25)]
    public void The_floor_is_half_your_level(int level, int expected) =>
        Assert.Equal(expected, XpRelevance.Floor(level));

    [Fact]
    public void Nothing_is_beneath_a_level_one()
    {
        // Floor(1) is 0 and mob levels start at 1, so a new character's first kill always counts.
        // Worth pinning: a rounding choice that made this 1 would silently zero the entire first
        // level of the game, which is the one stretch where every kill matters most.
        Assert.Equal(0, XpRelevance.Floor(1));
        Assert.Equal(1.0, XpRelevance.Fraction(1, 1));
    }

    [Fact]
    public void The_note_that_started_this_holds()
    {
        // "a level 10 shouldn't get exp for killing a level 1 mob" - PlayTestingNotes.
        Assert.Equal(0, XpRelevance.ShareOf(500, killerLevel: 10, mobLevel: 1));
    }

    [Theory]
    [InlineData(10, 10)]
    [InlineData(10, 15)]
    [InlineData(50, 50)]
    public void A_fair_fight_or_worse_pays_in_full(int killer, int mob) =>
        Assert.Equal(1.0, XpRelevance.Fraction(killer, mob));

    [Fact]
    public void Punching_up_pays_the_same_as_a_fair_fight()
    {
        // Deliberate, and worth a test so it is a decision rather than an oversight: rewarding a
        // fight above your level would be a separate design choice about what the game encourages,
        // and it should be made on purpose rather than fall out of a formula.
        Assert.Equal(XpRelevance.Fraction(20, 20), XpRelevance.Fraction(20, 40));
    }

    [Fact]
    public void The_floor_itself_still_pays_something()
    {
        // The floor is the last level that counts, not the first that does not - which is what
        // makes the same number work for the party rule, where a level 25 shares a level 50's
        // kills and a level 24 does not.
        Assert.True(XpRelevance.Fraction(10, 5) > 0);
        Assert.Equal(0.0, XpRelevance.Fraction(10, 4));
    }

    [Fact]
    public void The_taper_climbs_without_a_step_in_it()
    {
        // The value of a slope over a cutoff is entirely in there being no level where one more
        // level of mob is worth a jump. Asserted across the whole window rather than at its ends.
        var previous = 0.0;

        for (var mobLevel = XpRelevance.Floor(30); mobLevel <= 30; mobLevel++)
        {
            var fraction = XpRelevance.Fraction(30, mobLevel);

            Assert.True(fraction > previous, $"level {mobLevel} did not improve on {mobLevel - 1}");
            Assert.True(fraction <= 1.0);
            previous = fraction;
        }

        Assert.Equal(1.0, previous);
    }

    [Fact]
    public void A_kill_that_counts_never_rounds_away_to_nothing()
    {
        // Otherwise "no experience" means both "that was beneath you" and "that was worth 0.4",
        // and the second one reads as a bug. One experience is a small reward; zero is a rule.
        var award = XpRelevance.ShareOf(1, killerLevel: 50, mobLevel: 25);

        Assert.Equal(1, award);
    }

    [Fact]
    public void A_zone_multiplier_cannot_resurrect_a_trivial_kill()
    {
        // The load-bearing one. Multipliers are applied first (§4.4) and this is applied to the
        // result, so an eight-times-experience starter zone is not a farm for a level 50 - ten
        // times zero is still zero. Reverse the order and this is the best XP in the game.
        Assert.Equal(0, XpRelevance.ShareOf(80_000, killerLevel: 50, mobLevel: 3));
    }

    [Fact]
    public void Who_you_are_standing_next_to_is_not_an_input()
    {
        // There was briefly a party floor here: the highest level present set a minimum, and a
        // level 9 beside a level 20 earned nothing from a level 19 mob they would have been paid
        // in full for killing alone. Help with a fight you could have taken cannot be worth less
        // than taking it.
        //
        // The rule takes two levels now, and there is nowhere for a third to be passed. Kept as a
        // test rather than only as a deleted method, because the tempting fix for power levelling
        // is to reach for that floor again.
        var solo = XpRelevance.ShareOf(1000, killerLevel: 9, mobLevel: 19);

        Assert.Equal(1000, solo);
    }
}
