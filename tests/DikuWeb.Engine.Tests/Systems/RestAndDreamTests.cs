using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Systems;
using DikuWeb.Engine.Tests.Infrastructure;

namespace DikuWeb.Engine.Tests.Systems;

/// <summary>
/// Posture: what a character sitting or lying down may do, and what reaches them while asleep.
/// </summary>
public sealed class RestAndDreamTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");

    // -----------------------------------------------------------------------
    // Standing up first
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("sleep")]
    [InlineData("rest")]
    public void You_cannot_open_a_fight_from_the_floor(string posture)
    {
        // The defect this suite was written for. Movement refused a resting character and `attack`
        // did not, so a fight could be started and held to the end without ever standing - while
        // drawing the resting regen rate the whole time.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var player = harness.AddPlayer("Kael", West, level: 10);
        harness.AddMob("rat", West, health: 200);

        harness.Execute(player, posture);
        harness.Drain(player);

        harness.Execute(player, "attack rat");

        Assert.Equal(CombatState.Idle, player.Character.CombatState);
        Assert.Contains("stand", harness.DrainText(player), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("sleep")]
    [InlineData("rest")]
    public void You_cannot_cast_from_the_floor(string posture)
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var player = harness.AddPlayer("Kael", West, level: 10);
        harness.AddMob("rat", West, health: 200);

        harness.Execute(player, posture);
        harness.Drain(player);

        harness.Execute(player, "cast bolt rat");

        var said = harness.DrainText(player);
        Assert.Contains("stand", said, StringComparison.OrdinalIgnoreCase);

        // And it is refused as a *posture* problem rather than as an unknown ability - the refusal
        // lands before the ability is resolved, so the answer is about the player, not the spell.
        Assert.DoesNotContain("don't know", said, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("sleep")]
    [InlineData("rest")]
    public void You_cannot_walk_out_from_the_floor(string posture)
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var player = harness.AddPlayer("Kael", West);

        harness.Execute(player, posture);
        harness.Drain(player);

        harness.Execute(player, "east");

        Assert.Equal(West, player.Character.RoomKey);
        Assert.Contains("stand", harness.DrainText(player), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Standing_up_puts_all_three_back()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var player = harness.AddPlayer("Kael", West, level: 10);
        harness.AddMob("rat", West, health: 200);

        harness.Execute(player, "sleep");
        harness.Execute(player, "stand");
        harness.Drain(player);

        harness.Execute(player, "attack rat");

        Assert.Equal(CombatState.Fighting, player.Character.CombatState);
    }

    [Fact]
    public void The_refusal_names_the_posture_you_are_actually_in()
    {
        // "You must stand up first" is the wrong answer to a player who thinks they are standing.
        // Both states refuse, and each says which one it is.
        var asleep = WorldHarness.NewCharacter("Kael", West);
        asleep.RestState = CharacterRestState.Sleep;
        Assert.Contains("asleep", RestGate.Refuse(asleep)!, StringComparison.Ordinal);

        var sitting = WorldHarness.NewCharacter("Kael", West);
        sitting.RestState = CharacterRestState.Rest;
        Assert.Contains("sitting", RestGate.Refuse(sitting)!, StringComparison.Ordinal);

        var standing = WorldHarness.NewCharacter("Kael", West);
        standing.RestState = CharacterRestState.Stand;
        Assert.Null(RestGate.Refuse(standing));
    }

    // -----------------------------------------------------------------------
    // What reaches a sleeper
    // -----------------------------------------------------------------------

    [Fact]
    public void A_sleeping_player_is_not_shown_another_players_emote()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var sleeper = harness.AddPlayer("Kael", West);
        var awake = harness.AddPlayer("Ilse", West);

        harness.Execute(sleeper, "sleep");
        harness.Drain(sleeper);
        harness.Drain(awake);

        harness.Execute(awake, "emote waves a lantern about");

        Assert.DoesNotContain("lantern", harness.DrainText(sleeper), StringComparison.Ordinal);
    }

    [Fact]
    public void An_awake_player_in_the_same_room_still_sees_it()
    {
        // The filter is per-player, not per-room. One person dozing must not silence the room for
        // everybody else in it.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var sleeper = harness.AddPlayer("Kael", West);
        var awake = harness.AddPlayer("Ilse", West);
        var watcher = harness.AddPlayer("Bram", West);

        harness.Execute(sleeper, "sleep");
        harness.Drain(watcher);

        harness.Execute(awake, "emote waves a lantern about");

        Assert.Contains("lantern", harness.DrainText(watcher), StringComparison.Ordinal);
    }

    [Fact]
    public void Speech_still_gets_through_to_a_sleeper()
    {
        // Deliberate. An emote is something you do where people can see it; being shouted at is
        // how somebody wakes you, and filtering it would leave no way to reach a sleeping player.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var sleeper = harness.AddPlayer("Kael", West);
        var awake = harness.AddPlayer("Ilse", West);

        harness.Execute(sleeper, "sleep");
        harness.Drain(sleeper);

        harness.Execute(awake, "say wake up");

        Assert.Contains("wake up", harness.DrainText(sleeper), StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // Dreams
    // -----------------------------------------------------------------------

    [Fact]
    public void Falling_asleep_does_not_dream_immediately()
    {
        // Dropping off and dreaming on the same tick reads as a bug rather than as sleep.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var player = harness.AddPlayer("Kael", West);

        harness.Execute(player, "sleep");
        harness.Drain(player);

        DreamSystem.Tick(harness.World, 0);

        Assert.Equal(string.Empty, harness.DrainText(player));
    }

    [Fact]
    public void A_sleeper_dreams_once_every_five_minutes()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var player = harness.AddPlayer("Kael", West);

        harness.Execute(player, "sleep");
        harness.Drain(player);

        DreamSystem.Tick(harness.World, 0);
        Assert.Equal(string.Empty, harness.DrainText(player));

        // A minute short of due: still nothing.
        DreamSystem.Tick(harness.World, DreamSystem.IntervalPulses - 240);
        Assert.Equal(string.Empty, harness.DrainText(player));

        DreamSystem.Tick(harness.World, DreamSystem.IntervalPulses);
        Assert.Contains("You dream", harness.DrainText(player), StringComparison.Ordinal);

        // And not again until the next interval.
        DreamSystem.Tick(harness.World, DreamSystem.IntervalPulses + 240);
        Assert.Equal(string.Empty, harness.DrainText(player));

        DreamSystem.Tick(harness.World, DreamSystem.IntervalPulses * 2);
        Assert.Contains("You dream", harness.DrainText(player), StringComparison.Ordinal);
    }

    [Fact]
    public void Somebody_awake_never_dreams()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var player = harness.AddPlayer("Kael", West);
        harness.Drain(player);

        for (var pulse = 0L; pulse <= DreamSystem.IntervalPulses * 3; pulse += 240)
        {
            DreamSystem.Tick(harness.World, pulse);
        }

        Assert.Equal(string.Empty, harness.DrainText(player));
    }

    [Fact]
    public void Waking_up_and_sleeping_again_starts_a_fresh_five_minutes()
    {
        // The timer is cleared on waking. Without that, somebody who slept an hour ago and lies
        // down again dreams on the very next tick from a stale due-time.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var player = harness.AddPlayer("Kael", West);

        harness.Execute(player, "sleep");
        DreamSystem.Tick(harness.World, 0);
        DreamSystem.Tick(harness.World, DreamSystem.IntervalPulses * 5);
        harness.Drain(player);

        harness.Execute(player, "stand");
        DreamSystem.Tick(harness.World, DreamSystem.IntervalPulses * 5);

        harness.Execute(player, "sleep");
        harness.Drain(player);

        DreamSystem.Tick(harness.World, DreamSystem.IntervalPulses * 5);
        Assert.Equal(string.Empty, harness.DrainText(player));

        DreamSystem.Tick(harness.World, DreamSystem.IntervalPulses * 6);
        Assert.Contains("You dream", harness.DrainText(player), StringComparison.Ordinal);
    }

    [Fact]
    public void Two_sleepers_in_one_room_do_not_dream_the_same_thing()
    {
        // Chosen from the character id rather than at random, so this is a property rather than a
        // coincidence - and it is what stops a shared room reading like a broadcast.
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var one = harness.AddPlayer("Kael", West);
        var two = harness.AddPlayer("Ilse", West);

        harness.Execute(one, "sleep");
        harness.Execute(two, "sleep");
        harness.Drain(one);
        harness.Drain(two);

        DreamSystem.Tick(harness.World, 0);
        DreamSystem.Tick(harness.World, DreamSystem.IntervalPulses);

        var first = harness.DrainText(one);
        var second = harness.DrainText(two);

        Assert.Contains("You dream", first, StringComparison.Ordinal);
        Assert.Contains("You dream", second, StringComparison.Ordinal);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Somebody_does_dream_of_electric_sheep()
    {
        Assert.Contains(
            DreamSystem.Lines,
            line => line.Contains("electric sheep", StringComparison.Ordinal));
    }
}
