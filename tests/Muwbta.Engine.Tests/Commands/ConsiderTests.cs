using Muwbta.Domain.Worlds;
using Muwbta.Engine.Tests.Infrastructure;

namespace Muwbta.Engine.Tests.Commands;

/// <summary>
/// What <c>consider</c> tells you, and whether the reward agrees with it (PLAN.md §4.7).
/// </summary>
/// <remarks>
/// <c>consider</c> has existed since Phase 4 with nothing asserting a word of it — only that "c"
/// resolves to the verb. It was also quietly disagreeing with the experience rules: a fixed ±5
/// band against half-your-level, which happens to agree around level 10 and does not at level 50.
/// That is the shape of bug that survives playtesting, because the two halves are read minutes
/// apart and neither looks wrong on its own.
/// </remarks>
public sealed class ConsiderTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");

    private static (WorldHarness Harness, Engine.World.PlayerActor Player) Ready(int level)
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return (harness, harness.AddPlayer("Bram", West, level: level));
    }

    private static string Consider(int playerLevel, int mobLevel, int? zoneMinLevel = null)
    {
        var (harness, player) = Ready(playerLevel);
        if (zoneMinLevel is { } min)
        {
            harness.Zone.MinLevel = min;
        }

        harness.AddMob("rat", West, health: 100, level: mobLevel);
        harness.Execute(player, "consider rat");

        return harness.DrainText(player);
    }

    // -----------------------------------------------------------------------
    // Above your level — deliberately unchanged
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(10, 10, "evenly matched")]
    [InlineData(10, 11, "evenly matched")]
    [InlineData(10, 13, "It looks stronger")]
    [InlineData(10, 20, "It looks much stronger")]
    [InlineData(50, 55, "It looks much stronger")]
    public void Danger_still_reads_on_absolute_levels(int player, int mob, string expected)
    {
        // Danger does not scale with your level the way relevance does - five levels above you is
        // a hard fight at 10 and a hard fight at 50, so there is nothing here to make proportional.
        // This half was working and is left exactly alone.
        Assert.Contains(expected, Consider(player, mob), StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // Below your level — the reward window
    // -----------------------------------------------------------------------

    [Fact]
    public void A_mob_worth_nothing_says_so_before_you_swing()
    {
        // The whole point of consider is deciding whether to bother, so the answer to "is this
        // worth my time" belongs before the fight rather than in the silence after it.
        Assert.Contains(
            "nothing left for you to learn",
            Consider(playerLevel: 10, mobLevel: 1),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_old_fixed_band_disagreed_with_the_reward_at_high_level()
    {
        // A level 44 mob pays a level 50 more than three quarters of full experience, and the old
        // ±5 band called it "You are much stronger" - the warning and the reward describing two
        // different fights. This is the case that motivated matching them.
        Assert.Contains("evenly matched", Consider(50, 44), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(50, 30, "You are much stronger")]
    [InlineData(50, 38, "You are stronger")]
    [InlineData(10, 8, "You are stronger")]
    public void Inside_the_window_it_says_how_far_ahead_you_are(int player, int mob, string expected) =>
        Assert.Contains(expected, Consider(player, mob), StringComparison.Ordinal);

    // -----------------------------------------------------------------------
    // The one that matters
    // -----------------------------------------------------------------------

    [Fact]
    public void Consider_and_the_reward_never_disagree()
    {
        // The property, rather than a handful of examples: across the whole range below a level 30
        // character, "there is nothing to learn" appears exactly when the kill pays nothing.
        //
        // Asserted by actually killing each one, so this compares the two code paths rather than
        // two readings of the same function - which is where the original bug lived.
        var disagreements = new List<string>();

        for (var mobLevel = 1; mobLevel <= 30; mobLevel++)
        {
            var (harness, player) = Ready(30);
            var mob = harness.AddMob("rat", West, health: 1, level: mobLevel);
            mob.ResolvedXp = 1000;

            harness.Execute(player, "consider rat");
            var saidWorthless = harness.DrainText(player)
                .Contains("nothing left for you to learn", StringComparison.Ordinal);

            var before = player.Character.Xp;
            harness.Execute(player, "attack rat");
            harness.Pump(20);
            var paidNothing = player.Character.Xp == before;

            if (saidWorthless != paidNothing)
            {
                disagreements.Add(
                    $"level {mobLevel}: consider said worthless={saidWorthless}, reward paid nothing={paidNothing}");
            }
        }

        Assert.Empty(disagreements);
    }

    // -----------------------------------------------------------------------
    // The zone
    // -----------------------------------------------------------------------

    [Fact]
    public void A_zone_band_lifts_what_consider_reports()
    {
        // Reporting the template's level here would be the game warning you about a different mob
        // from the one it is about to reward you for.
        var text = Consider(playerLevel: 40, mobLevel: 1, zoneMinLevel: 40);

        Assert.Contains("Level 40", text, StringComparison.Ordinal);
        Assert.Contains("evenly matched", text, StringComparison.Ordinal);
        Assert.DoesNotContain("nothing left", text, StringComparison.Ordinal);
    }

    [Fact]
    public void It_names_the_mob_rather_than_its_key()
    {
        // Was "A city-rat — Level 1", interpolating the template key straight into player-facing
        // prose - the one thing §9 says every line about a mob must not do.
        var (harness, player) = Ready(5);
        harness.AddMob("city-rat", West, health: 100, name: "city rat", level: 5);

        harness.Execute(player, "consider city rat");

        var text = harness.DrainText(player);
        Assert.Contains("A city rat", text, StringComparison.Ordinal);
        Assert.DoesNotContain("city-rat", text, StringComparison.Ordinal);
    }

}
