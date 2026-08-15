using DikuWeb.Domain.Entities;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Tests.Infrastructure;

namespace DikuWeb.Engine.Tests.Commands;

/// <summary>
/// Joining the fight somebody else is already in.
/// </summary>
/// <remarks>
/// The verb exists because names cannot aim: two crows in a room answer to the same word, and the
/// tie is broken on arrival order — which has nothing to do with which one your party is fighting.
/// Naming the person is stable, because the person is who you meant.
/// </remarks>
public sealed class AssistTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");
    private static readonly RoomKey East = RoomKey.Parse("test.zone.east");

    [Fact]
    public void It_inherits_the_target_rather_than_resolving_a_name()
    {
        // The case that motivated it. Kael is on the *second* crow, which is the one `attack crow`
        // would never find — so if this landed on the first one, assist would be doing nothing the
        // player could not already do wrong by themselves.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.AddMob("terrace-crow", West, name: "a terrace crow", health: 500);
        var second = harness.AddMob("terrace-crow", West, name: "a terrace crow", health: 500);

        var kael = harness.AddPlayer("Kael", West);
        var ilse = harness.AddPlayer("Ilse", West);

        harness.Execute(kael, "attack crow 2");
        harness.Drain(ilse);

        harness.Execute(ilse, "assist Kael");

        Assert.Equal(EntityId.ForMob(second.Id), ilse.Character.CurrentTarget);
        Assert.Equal(kael.Character.CurrentTarget, ilse.Character.CurrentTarget);
    }

    [Fact]
    public void Somebody_who_is_not_fighting_is_said_so()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.AddMob("terrace-crow", West, name: "a terrace crow", health: 500);

        var kael = harness.AddPlayer("Kael", West);
        var ilse = harness.AddPlayer("Ilse", West);
        harness.Drain(ilse);

        harness.Execute(ilse, "assist Kael");

        Assert.Null(ilse.Character.CurrentTarget);
        Assert.Contains(
            "Kael isn't attacking anyone.",
            harness.DrainText(ilse),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_target_that_has_died_reads_as_not_attacking()
    {
        // True from where the assisting player is standing, and better than explaining that a
        // stale entity id is pointing at nothing.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var crow = harness.AddMob("terrace-crow", West, name: "a terrace crow", health: 500);

        var kael = harness.AddPlayer("Kael", West);
        var ilse = harness.AddPlayer("Ilse", West);

        harness.Execute(kael, "attack crow");
        harness.World.RemoveMob(crow);
        harness.Drain(ilse);

        harness.Execute(ilse, "assist Kael");

        Assert.Null(ilse.Character.CurrentTarget);
        Assert.Contains(
            "Kael isn't attacking anyone.",
            harness.DrainText(ilse),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Somebody_fighting_in_another_room_cannot_be_assisted()
    {
        // OthersIn is per-room, so this is the plain "not here" refusal rather than a combat one.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.AddMob("terrace-crow", East, name: "a terrace crow", health: 500);

        var kael = harness.AddPlayer("Kael", East);
        var ilse = harness.AddPlayer("Ilse", West);

        harness.Execute(kael, "attack crow");
        harness.Drain(ilse);

        harness.Execute(ilse, "assist Kael");

        Assert.Null(ilse.Character.CurrentTarget);
        Assert.Contains("don't see", harness.DrainText(ilse), StringComparison.Ordinal);
    }

    [Fact]
    public void It_refuses_mid_fight_the_way_attack_does()
    {
        // Switching targets is a separate decision from choosing one. If that rule is ever
        // relaxed it should be relaxed for both verbs at once, which is what this pins.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.AddMob("terrace-crow", West, name: "a terrace crow", health: 500);
        var second = harness.AddMob("terrace-crow", West, name: "a terrace crow", health: 500);

        var kael = harness.AddPlayer("Kael", West);
        var ilse = harness.AddPlayer("Ilse", West);

        harness.Execute(kael, "attack crow 2");
        harness.Execute(ilse, "attack crow");
        var already = ilse.Character.CurrentTarget;
        harness.Drain(ilse);

        harness.Execute(ilse, "assist Kael");

        Assert.Equal(already, ilse.Character.CurrentTarget);
        Assert.NotEqual(EntityId.ForMob(second.Id), ilse.Character.CurrentTarget);
        Assert.Contains("already in combat", harness.DrainText(ilse), StringComparison.Ordinal);
    }

    [Fact]
    public void Assist_with_no_argument_asks_whom()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var ilse = harness.AddPlayer("Ilse", West);
        harness.Drain(ilse);

        harness.Execute(ilse, "assist");

        Assert.Contains("Assist whom?", harness.DrainText(ilse), StringComparison.Ordinal);
    }

    [Fact]
    public void The_mob_gets_a_second_person_on_its_hate_list()
    {
        // Assisting has to be a real engagement, not just a pointed sword: without AddCombatant the
        // mob never gets a hate list entry for the newcomer, and every point of threat after it
        // vanishes silently.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var crow = harness.AddMob("terrace-crow", West, name: "a terrace crow", health: 500);

        var kael = harness.AddPlayer("Kael", West);
        var ilse = harness.AddPlayer("Ilse", West);

        harness.Execute(kael, "attack crow");
        harness.Execute(ilse, "assist Kael");

        var combat = harness.World.GetOrCreateCombat(West);
        var crowId = EntityId.ForMob(crow.Id);

        Assert.True(combat.HateOf(crowId, EntityId.ForCharacter(kael.CharacterId)) > 0);
        Assert.True(combat.HateOf(crowId, EntityId.ForCharacter(ilse.CharacterId)) > 0);
    }
}
