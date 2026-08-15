using DikuWeb.Domain.Entities;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Tests.Infrastructure;

namespace DikuWeb.Engine.Tests.Systems;

/// <summary>
/// Two mobs in a room answering to the same word.
/// </summary>
/// <remarks>
/// Asked from play: one player attacks a crow, a second crow wanders in, and the other player
/// attacks a few seconds later — did the second attack go to the newcomer? These pin the answer,
/// because nothing in the transcript can tell you: both mobs narrate as "a terrace crow", so every
/// line either player sees is identical whichever bird they hit.
///
/// The guarantee is the composition of two rules that live apart, which is why it is worth a test
/// rather than an argument: <c>NameMatch.Best</c> keeps the <em>earlier</em> candidate on a tie,
/// and <c>WorldState.MoveMob</c> <em>appends</em> to the destination room's list. A mob that walks
/// in is therefore always last and can never win a tie against one already standing there.
///
/// If the default is ever changed to prefer a mob not already in combat, these are the tests that
/// should fail — deliberately.
/// </remarks>
public sealed class DuplicateTargetTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");
    private static readonly RoomKey East = RoomKey.Parse("test.zone.east");

    [Fact]
    public void A_mob_that_wanders_in_does_not_steal_the_second_attack()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        // The resident, and the one that walks in afterwards. Same template, same name, so the
        // word "crow" ranks identically against both.
        var resident = harness.AddMob("terrace-crow", West, name: "a terrace crow", health: 500);
        var newcomer = harness.AddMob("terrace-crow", East, name: "a terrace crow", health: 500);

        var kael = harness.AddPlayer("Kael", West);
        var ilse = harness.AddPlayer("Ilse", West);

        harness.Execute(kael, "attack crow");

        // Wanders in, the way MobAiSystem moves it.
        harness.World.MoveMob(newcomer, West);

        harness.Execute(ilse, "attack crow");

        Assert.Equal(EntityId.ForMob(resident.Id), kael.Character.CurrentTarget);
        Assert.Equal(kael.Character.CurrentTarget, ilse.Character.CurrentTarget);
        Assert.NotEqual(EntityId.ForMob(newcomer.Id), ilse.Character.CurrentTarget);
    }

    [Fact]
    public void Both_players_pick_the_same_one_when_both_were_already_standing_there()
    {
        // The simpler half, and the one that says the tie-break is arrival order rather than
        // anything about the fight: no wandering involved, still the same bird.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var first = harness.AddMob("terrace-crow", West, name: "a terrace crow", health: 500);
        harness.AddMob("terrace-crow", West, name: "a terrace crow", health: 500);

        var kael = harness.AddPlayer("Kael", West);
        var ilse = harness.AddPlayer("Ilse", West);

        harness.Execute(kael, "attack crow");
        harness.Execute(ilse, "attack crow");

        Assert.Equal(EntityId.ForMob(first.Id), kael.Character.CurrentTarget);
        Assert.Equal(EntityId.ForMob(first.Id), ilse.Character.CurrentTarget);
    }
}
