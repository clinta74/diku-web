using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Tests.Infrastructure;

namespace DikuWeb.Engine.Tests.Commands;

/// <summary>
/// Talking to someone who is not in the room (PLAN.md §5.3).
/// </summary>
public sealed class ChannelCommandTests
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
    public void A_tell_crosses_rooms()
    {
        // Otherwise it is `say` with extra steps.
        var harness = Loaded();
        var sender = harness.AddPlayer("Bram", West);
        var target = harness.AddPlayer("Kael", East);

        harness.Execute(sender, "tell Kael are you still down there");

        Assert.Contains(
            "Bram tells you, 'are you still down there'",
            harness.DrainText(target),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_tell_reaches_nobody_else()
    {
        var harness = Loaded();
        var sender = harness.AddPlayer("Bram", West);
        harness.AddPlayer("Kael", West);
        var bystander = harness.AddPlayer("Vurn", West);

        harness.Execute(sender, "tell Kael meet me at the gate");

        Assert.DoesNotContain("gate", harness.DrainText(bystander), StringComparison.Ordinal);
    }

    [Fact]
    public void A_name_that_is_not_online_is_refused()
    {
        var harness = Loaded();
        var sender = harness.AddPlayer("Bram", West);

        harness.Execute(sender, "tell Nobody hello");

        Assert.Contains("is online", harness.DrainText(sender), StringComparison.Ordinal);
    }

    [Fact]
    public void Reply_answers_whoever_told_you_last()
    {
        var harness = Loaded();
        var first = harness.AddPlayer("Bram", West);
        var second = harness.AddPlayer("Kael", East);

        harness.Execute(first, "tell Kael where are you");
        harness.Drain(first);

        harness.Execute(second, "reply the crypt");

        Assert.Contains(
            "Kael tells you, 'the crypt'",
            harness.DrainText(first),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Reply_with_nobody_to_answer_is_refused()
    {
        var harness = Loaded();
        var lonely = harness.AddPlayer("Bram", West);

        harness.Execute(lonely, "reply hello");

        Assert.Contains("Nobody has told you", harness.DrainText(lonely), StringComparison.Ordinal);
    }

    [Fact]
    public void Reply_follows_the_most_recent_teller()
    {
        var harness = Loaded();
        var target = harness.AddPlayer("Bram", West);
        var first = harness.AddPlayer("Kael", East);
        var second = harness.AddPlayer("Vurn", East);

        harness.Execute(first, "tell Bram it is me");
        harness.Execute(second, "tell Bram no it is me");
        harness.Drain(first);
        harness.Drain(second);

        harness.Execute(target, "reply which of you");

        Assert.DoesNotContain("which of you", harness.DrainText(first), StringComparison.Ordinal);
        Assert.Contains("which of you", harness.DrainText(second), StringComparison.Ordinal);
    }

    [Fact]
    public void The_sender_is_told_when_the_listener_is_link_dead()
    {
        // §3.6 leaves a link-dead character standing in the room, so they look present. Saying so
        // costs nothing and stops the sender waiting on an answer.
        var harness = Loaded();
        var sender = harness.AddPlayer("Bram", West);
        var absent = harness.AddPlayer("Kael", West);
        absent.Output = null;

        harness.Execute(sender, "tell Kael are you there");

        Assert.Contains("somewhere else entirely", harness.DrainText(sender), StringComparison.Ordinal);
    }

    [Fact]
    public void Chat_reaches_the_whole_world()
    {
        var harness = Loaded();
        var speaker = harness.AddPlayer("Bram", West);
        var far = harness.AddPlayer("Kael", East);

        harness.Execute(speaker, "chat anyone selling a temper");

        Assert.Contains(
            "Bram chats, 'anyone selling a temper'",
            harness.DrainText(far),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Chat_off_stops_you_hearing_it()
    {
        var harness = Loaded();
        var speaker = harness.AddPlayer("Bram", West);
        var quiet = harness.AddPlayer("Kael", East);

        harness.Execute(quiet, "chat off");
        harness.Drain(quiet);

        harness.Execute(speaker, "chat anyone selling a temper");

        Assert.DoesNotContain("temper", harness.DrainText(quiet), StringComparison.Ordinal);
    }

    [Fact]
    public void Chat_off_stops_you_posting_to_it_as_well()
    {
        // A channel you can shout into while ignoring the replies is not one anybody else wants
        // to share.
        var harness = Loaded();
        var quiet = harness.AddPlayer("Bram", West);
        var listener = harness.AddPlayer("Kael", East);

        harness.Execute(quiet, "chat off");
        harness.Execute(quiet, "chat anyone selling a temper");

        Assert.DoesNotContain("temper", harness.DrainText(listener), StringComparison.Ordinal);
        Assert.Contains("world channel is off", harness.DrainText(quiet), StringComparison.Ordinal);
    }

    [Fact]
    public void Chat_on_puts_it_back()
    {
        var harness = Loaded();
        var speaker = harness.AddPlayer("Bram", West);
        var quiet = harness.AddPlayer("Kael", East);

        harness.Execute(quiet, "chat off");
        harness.Execute(quiet, "chat on");
        harness.Drain(quiet);

        harness.Execute(speaker, "chat anyone selling a temper");

        Assert.Contains("temper", harness.DrainText(quiet), StringComparison.Ordinal);
    }

    [Fact]
    public void The_short_forms_do_not_steal_an_older_verb()
    {
        // Prefix matching is first-match-wins and the older verb keeps the shorter abbreviation:
        // "t" is still talk, "r" is still rest, "c" is still consider, "g" is still get.
        Assert.Equal("talk", Verb("t"));
        Assert.Equal("tell", Verb("te"));
        Assert.Equal("rest", Verb("r"));
        Assert.Equal("recall", Verb("rec"));
        Assert.Equal("reply", Verb("rep"));
        Assert.Equal("consider", Verb("c"));
        Assert.Equal("chat", Verb("ch"));
        Assert.Equal("get", Verb("g"));
        Assert.Equal("group", Verb("gr"));
        Assert.Equal("gtell", Verb("gt"));
    }

    private static string Verb(string typed) =>
        new WorldHarness().Commands.Find(typed)?.Name
            ?? throw new InvalidOperationException($"Nothing matched '{typed}'.");
}
