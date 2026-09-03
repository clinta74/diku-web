using Muwbta.Domain.Characters;
using Muwbta.Domain.Combat;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Mutations;
using Muwbta.Engine.Tests.Infrastructure;

namespace Muwbta.Engine.Tests.Commands;

/// <summary>
/// Setting a bind point, and the <c>respawn</c> flag that decides where you may (PLAN.md §4.12).
/// </summary>
/// <remarks>
/// <b>Half of these assert what <em>does not</em> refuse it.</b> <c>bind</c> takes exactly one
/// clause from <see cref="Engine.Systems.Travel.Refuse"/> — the fight — and deliberately leaves the
/// rest, because binding is not travel: it moves nobody. A later pass tidying the two verbs into
/// agreement would silently make it impossible to bind while seated, or inside the one kind of
/// region where knowing where you wake up matters most. The divergence is the design, so it is
/// tested rather than left to a comment.
/// </remarks>
public sealed class BindTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");
    private static readonly RoomKey East = RoomKey.Parse("test.zone.east");

    private static WorldHarness Loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return harness;
    }

    /// <summary>A harness whose <paramref name="room"/> accepts binding.</summary>
    private static WorldHarness Bindable(RoomKey room)
    {
        var harness = Loaded();
        harness.Mutate(new SetRoomFlag(room, RoomFlags.Respawn.Key, true));
        return harness;
    }

    [Fact]
    public void Binding_sets_the_respawn_point_to_the_room_you_are_in()
    {
        var harness = Bindable(East);
        var actor = harness.AddPlayer("Bram", East);

        harness.Execute(actor, "bind");

        Assert.Equal(East, actor.Character.RespawnRoomKey);
    }

    [Fact]
    public void An_unflagged_room_refuses_it()
    {
        // Absence is the safe value (§4.10), so a room nobody has thought about is not a waypoint.
        var harness = Loaded();
        var actor = harness.AddPlayer("Bram", East);

        harness.Execute(actor, "bind");

        Assert.Null(actor.Character.RespawnRoomKey);
        Assert.Contains("cannot bind", harness.DrainText(actor), StringComparison.Ordinal);
    }

    [Fact]
    public void The_flag_one_scope_up_is_enough()
    {
        // Flags resolve nearest-level-wins (§4.10), which is what lets a hub zone be declared
        // bindable once rather than room by room — the granularity §4.12 actually wants.
        var harness = Loaded();
        harness.Mutate(new SetZoneFlag("test.zone", RoomFlags.Respawn.Key, true));

        var actor = harness.AddPlayer("Bram", East);

        harness.Execute(actor, "bind");

        Assert.Equal(East, actor.Character.RespawnRoomKey);
    }

    [Fact]
    public void Rebinding_names_the_point_it_replaced()
    {
        // The verb is one keystroke and overwrites without asking. Saying what was lost is the
        // whole guard: otherwise a stray `b` is invisible until the next time you die.
        var harness = Bindable(East);
        var actor = harness.AddPlayer("Bram", East);
        actor.Character.RespawnRoomKey = West;

        harness.Execute(actor, "bind");

        Assert.Equal(East, actor.Character.RespawnRoomKey);

        // By name, not by key. It used to assert the raw "test.zone.west", which is an authoring
        // identifier and exactly what a player should never be shown (BUGS.md #14) - so the old
        // assertion was pinning the leak rather than the behaviour it was written for.
        var text = harness.DrainText(actor);
        Assert.Contains("The west room", text, StringComparison.Ordinal);
        Assert.DoesNotContain(West.ToString(), text, StringComparison.Ordinal);
    }

    [Fact]
    public void Binding_where_you_are_already_bound_is_refused_rather_than_narrated()
    {
        var harness = Bindable(East);
        var actor = harness.AddPlayer("Bram", East);
        actor.Character.RespawnRoomKey = East;

        harness.Execute(actor, "bind");

        Assert.Contains("already", harness.DrainText(actor), StringComparison.Ordinal);
    }

    [Fact]
    public void A_fight_refuses_it()
    {
        var harness = Bindable(East);
        var actor = harness.AddPlayer("Bram", East);
        harness.AddMob("rat", East, health: 100);

        harness.Execute(actor, "attack rat");
        Assert.Equal(CombatState.Fighting, actor.Character.CombatState);

        harness.Execute(actor, "bind");

        Assert.Null(actor.Character.RespawnRoomKey);
        Assert.Contains("flee", harness.DrainText(actor), StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // What deliberately does not refuse it
    // -----------------------------------------------------------------------

    [Fact]
    public void NoRecall_does_not_refuse_it()
    {
        // `noRecall` refuses travel out (§5.3). Binding goes nowhere, and a sealed region is
        // precisely where a player most wants to say where they would rather wake up.
        var harness = Bindable(East);
        harness.Mutate(new SetRoomFlag(East, RoomFlags.NoRecall.Key, true));

        var actor = harness.AddPlayer("Bram", East);

        harness.Execute(actor, "bind");

        Assert.Equal(East, actor.Character.RespawnRoomKey);
    }

    [Fact]
    public void Sitting_down_does_not_refuse_it()
    {
        var harness = Bindable(East);
        var actor = harness.AddPlayer("Bram", East);
        actor.Character.RestState = CharacterRestState.Rest;

        harness.Execute(actor, "bind");

        Assert.Equal(East, actor.Character.RespawnRoomKey);
    }
}
