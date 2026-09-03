using Muwbta.Domain.Characters;
using Muwbta.Domain.Combat;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Mutations;
using Muwbta.Engine.Tests.Infrastructure;

namespace Muwbta.Engine.Tests.Commands;

/// <summary>
/// Recall, and the <c>noRecall</c> flag it finally gives something to refuse (PLAN.md §5.3).
/// </summary>
/// <remarks>
/// The flag has been registered since Phase 4 with no reader, which §4.10 calls dead weight. It
/// was not an oversight so much as a listing problem: it is dead weight <em>until the verb
/// exists</em>. Every test here that asserts a refusal is asserting that the one reader is
/// actually consulted, because a second travel verb landing later and forgetting to ask is how a
/// flag becomes a lie.
/// </remarks>
public sealed class RecallTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");
    private static readonly RoomKey East = RoomKey.Parse("test.zone.east");
    private static readonly RoomKey Middle = RoomKey.Parse("test.zone.middle");

    private static WorldHarness Loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return harness;
    }

    [Fact]
    public void Recall_returns_you_to_where_you_bound()
    {
        var harness = Loaded();
        var actor = harness.AddPlayer("Bram", East);
        actor.Character.RespawnRoomKey = West;

        harness.Execute(actor, "recall");

        Assert.Equal(West, actor.RoomKey);
    }

    [Fact]
    public void An_unbound_character_falls_back_to_the_starting_room()
    {
        // Which is also where §7.4 puts anyone whose saved room stopped existing.
        var harness = Loaded();
        var actor = harness.AddPlayer("Bram", East);

        harness.Execute(actor, "recall");

        Assert.Equal(harness.Options.StartingRoom, actor.RoomKey);
    }

    [Fact]
    public void A_bind_point_a_builder_deleted_falls_back_too()
    {
        var harness = Loaded();
        var actor = harness.AddPlayer("Bram", East);
        actor.Character.RespawnRoomKey = RoomKey.Parse("test.zone.gone");

        harness.Execute(actor, "recall");

        Assert.Equal(harness.Options.StartingRoom, actor.RoomKey);
    }

    [Fact]
    public void The_room_you_left_and_the_room_you_arrive_in_both_see_it()
    {
        var harness = Loaded();
        var actor = harness.AddPlayer("Bram", East);
        var watcher = harness.AddPlayer("Kael", East);
        var greeter = harness.AddPlayer("Vurn", West);

        actor.Character.RespawnRoomKey = West;
        harness.Drain(watcher);
        harness.Drain(greeter);

        harness.Execute(actor, "recall");

        Assert.Contains("is gone", harness.DrainText(watcher), StringComparison.Ordinal);
        Assert.Contains("appears", harness.DrainText(greeter), StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // What refuses it
    // -----------------------------------------------------------------------

    [Fact]
    public void NoRecall_refuses_it()
    {
        var harness = Loaded();
        harness.Mutate(new SetRoomFlag(East, RoomFlags.NoRecall.Key, true));

        var actor = harness.AddPlayer("Bram", East);
        actor.Character.RespawnRoomKey = West;

        harness.Execute(actor, "recall");

        Assert.Equal(East, actor.RoomKey);
        Assert.Contains("You must walk", harness.DrainText(actor), StringComparison.Ordinal);
    }

    [Fact]
    public void NoRecall_one_scope_up_is_enough()
    {
        // Flags resolve nearest-level-wins (§4.10), so a sealed dungeon is flagged once on the
        // zone rather than on every room in it.
        var harness = Loaded();
        harness.Mutate(new SetZoneFlag("test.zone", RoomFlags.NoRecall.Key, true));

        var actor = harness.AddPlayer("Bram", East);
        actor.Character.RespawnRoomKey = West;

        harness.Execute(actor, "recall");

        Assert.Equal(East, actor.RoomKey);
    }

    [Fact]
    public void A_fight_refuses_it()
    {
        // Travel is what you do between fights. `flee` stays the one way out of one, and keeps
        // its cost — otherwise recall is a free escape from every fight you are losing.
        var harness = Loaded();
        var actor = harness.AddPlayer("Bram", East);
        actor.Character.RespawnRoomKey = West;
        harness.AddMob("rat", East, health: 100);

        harness.Execute(actor, "kill rat");
        Assert.Equal(CombatState.Fighting, actor.Character.CombatState);

        harness.Execute(actor, "recall");

        Assert.Equal(East, actor.RoomKey);
        Assert.Contains("flee", harness.DrainText(actor), StringComparison.Ordinal);
    }

    [Fact]
    public void Sitting_down_refuses_it()
    {
        var harness = Loaded();
        var actor = harness.AddPlayer("Bram", East);
        actor.Character.RespawnRoomKey = West;
        actor.Character.RestState = CharacterRestState.Rest;

        harness.Execute(actor, "recall");

        Assert.Equal(East, actor.RoomKey);
    }

    [Fact]
    public void Already_being_there_is_refused_rather_than_narrated()
    {
        var harness = Loaded();
        var actor = harness.AddPlayer("Bram", Middle);
        actor.Character.RespawnRoomKey = Middle;

        harness.Execute(actor, "recall");

        Assert.Contains("already", harness.DrainText(actor), StringComparison.Ordinal);
    }
}
