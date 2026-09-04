using Muwbta.Domain.Accounts;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Commands;
using Muwbta.Engine.Tests.Infrastructure;

namespace Muwbta.Engine.Tests.Commands;

/// <summary>
/// A player can stop hearing from somebody without waiting for a moderator.
/// </summary>
public sealed class IgnoreTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");
    private static readonly RoomKey East = RoomKey.Parse("test.zone.east");

    private static WorldHarness Loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return harness;
    }

    [Fact]
    public void An_ignored_tell_does_not_arrive_and_the_sender_is_told()
    {
        // Told rather than silently dropped: a tell that vanishes reads as a bug, and a pest who
        // thinks the game is broken keeps trying.
        var harness = Loaded();
        var pest = harness.AddPlayer("Bram", West);
        var target = harness.AddPlayer("Kael", East);

        harness.Execute(target, "ignore Bram");
        harness.Drain(target);

        harness.Execute(pest, "tell Kael hello again");

        Assert.DoesNotContain("hello again", harness.DrainText(target), StringComparison.Ordinal);
        Assert.Contains("not listening to you", harness.DrainText(pest), StringComparison.Ordinal);
    }

    [Fact]
    public void Room_speech_and_emotes_from_an_ignored_player_are_not_heard()
    {
        var harness = Loaded();
        var pest = harness.AddPlayer("Bram", West);
        var target = harness.AddPlayer("Kael", West);
        var bystander = harness.AddPlayer("Vurn", West);

        harness.Execute(target, "ignore Bram");
        harness.Drain(target);
        harness.Drain(bystander);

        harness.Execute(pest, "say can you hear me");
        harness.Execute(pest, "emote waves frantically");

        var heardByTarget = harness.DrainText(target);
        Assert.DoesNotContain("can you hear me", heardByTarget, StringComparison.Ordinal);
        Assert.DoesNotContain("waves frantically", heardByTarget, StringComparison.Ordinal);

        // Only the one who asked stops hearing them. Everybody else still does.
        var heardByBystander = harness.DrainText(bystander);
        Assert.Contains("can you hear me", heardByBystander, StringComparison.Ordinal);
        Assert.Contains("waves frantically", heardByBystander, StringComparison.Ordinal);
    }

    [Fact]
    public void The_world_channel_is_covered_too()
    {
        var harness = Loaded();
        var pest = harness.AddPlayer("Bram", West);
        var target = harness.AddPlayer("Kael", East);

        harness.Execute(target, "ignore Bram");
        harness.Drain(target);

        harness.Execute(pest, "chat anyone around");

        Assert.DoesNotContain("anyone around", harness.DrainText(target), StringComparison.Ordinal);
    }

    [Fact]
    public void Movement_is_still_seen()
    {
        // Ignoring is about what is said to you, not about pretending somebody is not in the
        // room - a player fighting somebody they cannot see is worse than one hearing them.
        // Same room, so it is the leaving that is narrated to the listener - the test world is
        // three rooms in a line, and where "east" leads is not this test's business.
        var harness = Loaded();
        var pest = harness.AddPlayer("Bram", West);
        var target = harness.AddPlayer("Kael", West);

        harness.Execute(target, "ignore Bram");
        harness.Drain(target);

        harness.Execute(pest, "east");

        Assert.Contains("Bram", harness.DrainText(target), StringComparison.Ordinal);
    }

    [Fact]
    public void Unignore_restores_them_and_the_list_shows_who_is_on_it()
    {
        var harness = Loaded();
        var pest = harness.AddPlayer("Bram", West);
        var target = harness.AddPlayer("Kael", East);

        harness.Execute(target, "ignore Bram");
        harness.Execute(target, "ignore");
        Assert.Contains("Not listening to: Bram", harness.DrainText(target), StringComparison.Ordinal);

        harness.Execute(target, "unignore bram");
        harness.Drain(target);

        harness.Execute(pest, "tell Kael back again");
        Assert.Contains("back again", harness.DrainText(target), StringComparison.Ordinal);
    }

    [Fact]
    public void Staff_cannot_be_ignored_and_are_delivered_regardless()
    {
        // A moderator telling you to stop is not a conversation you get to opt out of.
        var harness = Loaded();
        var moderator = harness.AddPlayer("Bram", West);
        moderator.Role = AccountRole.Moderator;
        var target = harness.AddPlayer("Kael", East);

        harness.Execute(target, "ignore Bram");
        Assert.Contains("cannot ignore staff", harness.DrainText(target), StringComparison.Ordinal);

        // Even a name added before the promotion is delivered once the sender is staff.
        target.Character.IgnoredNames.Add("Bram");
        harness.Execute(moderator, "tell Kael that is enough");
        Assert.Contains("that is enough", harness.DrainText(target), StringComparison.Ordinal);
    }

    [Fact]
    public void You_cannot_ignore_yourself_and_the_list_has_a_ceiling()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", West);

        harness.Execute(kael, "ignore Kael");
        Assert.Contains("cannot ignore yourself", harness.DrainText(kael), StringComparison.Ordinal);

        for (var i = 0; i < IgnoreCommands.MaxIgnored; i++)
        {
            kael.Character.IgnoredNames.Add("Pest" + (char)('a' + i % 26) + (char)('a' + i / 26));
        }

        harness.Execute(kael, "ignore Bram");
        Assert.Contains("tell a moderator", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void The_list_is_carried_in_the_snapshot()
    {
        // This is the whole of what reaches the database, and three fields have already been lost
        // by being added to the character and not to this record.
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", West);
        harness.Execute(kael, "ignore Bram");

        var snapshot = CharacterSnapshot.From(kael.Character, DateTimeOffset.UnixEpoch);

        Assert.Equal(["Bram"], snapshot.IgnoredNames);
    }
}
