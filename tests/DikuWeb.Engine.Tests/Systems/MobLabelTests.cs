using DikuWeb.Domain.Entities;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Tests.Infrastructure;

namespace DikuWeb.Engine.Tests.Systems;

/// <summary>
/// Telling two identical mobs apart, and typing at the one you meant.
/// </summary>
/// <remarks>
/// The label and the lookup are two halves of one thing: a "(2)" a player can read but not act on
/// sends them to "you don't see that here", which is worse than not numbering them at all. So both
/// are tested together, and the property that matters is that they <em>agree</em> — the mob shown
/// as (2) is the mob <c>crow 2</c> reaches.
/// </remarks>
public sealed class MobLabelTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");

    // -----------------------------------------------------------------------
    // The label
    // -----------------------------------------------------------------------

    [Fact]
    public void A_mob_with_no_twin_is_not_numbered()
    {
        // An ordinal on a thing with nothing to disambiguate is noise on every line in the game
        // for the sake of the rare room where it matters.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.AddMob("terrace-crow", West, name: "a terrace crow");

        var kael = harness.AddPlayer("Kael", West);
        harness.Drain(kael);

        harness.Execute(kael, "look");

        var text = harness.DrainText(kael);
        Assert.Contains("A terrace crow is here.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("(1)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_of_a_kind_are_numbered_in_arrival_order()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.AddMob("terrace-crow", West, name: "a terrace crow");
        harness.AddMob("terrace-crow", West, name: "a terrace crow");

        var kael = harness.AddPlayer("Kael", West);
        harness.Drain(kael);

        harness.Execute(kael, "look");

        var text = harness.DrainText(kael);
        Assert.Contains("A terrace crow (1) is here.", text, StringComparison.Ordinal);
        Assert.Contains("A terrace crow (2) is here.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Different_names_need_no_numbering()
    {
        // Keyed on what is displayed rather than on the template: two rats a player can tell apart
        // are not two of the same thing.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.AddMob("barn-rat", West, name: "a barn rat");
        harness.AddMob("cave-rat", West, name: "a cave rat");

        var kael = harness.AddPlayer("Kael", West);
        harness.Drain(kael);

        harness.Execute(kael, "look");

        Assert.DoesNotContain("(1)", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void Combat_narration_carries_the_label()
    {
        // The line that made this necessary. Without it, two players fighting two crows read four
        // identical sentences and cannot tell whether they are on the same bird.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.AddMob("terrace-crow", West, name: "a terrace crow", health: 500);
        harness.AddMob("terrace-crow", West, name: "a terrace crow", health: 500);

        var kael = harness.AddPlayer("Kael", West);
        harness.Drain(kael);

        harness.Execute(kael, "attack crow");

        Assert.Contains(
            "You begin attacking a terrace crow (1)!",
            harness.DrainText(kael),
            StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // Typing the ordinal
    // -----------------------------------------------------------------------

    [Fact]
    public void A_trailing_number_reaches_the_one_it_labels()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.AddMob("terrace-crow", West, name: "a terrace crow", health: 500);
        var second = harness.AddMob("terrace-crow", West, name: "a terrace crow", health: 500);

        var kael = harness.AddPlayer("Kael", West);
        harness.Drain(kael);

        harness.Execute(kael, "attack crow 2");

        Assert.Equal(EntityId.ForMob(second.Id), kael.Character.CurrentTarget);
        Assert.Contains(
            "a terrace crow (2)",
            harness.DrainText(kael),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Asking_for_one_that_is_not_there_refuses()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.AddMob("terrace-crow", West, name: "a terrace crow", health: 500);

        var kael = harness.AddPlayer("Kael", West);
        harness.Drain(kael);

        harness.Execute(kael, "attack crow 2");

        Assert.Null(kael.Character.CurrentTarget);
        Assert.Contains("don't see", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void A_name_that_ends_in_a_number_still_matches_whole()
    {
        // The ordinal reading is a fallback rather than a parse, precisely so an authored name
        // ending in a digit keeps working. Matched on the first pass, before any splitting.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var guard = harness.AddMob("gate-guard-2", West, name: "guard 2", health: 500);
        harness.AddMob("gate-guard-1", West, name: "guard 1", health: 500);

        var kael = harness.AddPlayer("Kael", West);
        harness.Drain(kael);

        harness.Execute(kael, "attack guard 2");

        Assert.Equal(EntityId.ForMob(guard.Id), kael.Character.CurrentTarget);
    }
}
