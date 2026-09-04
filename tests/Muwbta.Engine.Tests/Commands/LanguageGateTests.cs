using Muwbta.Domain.Worlds;
using Muwbta.Engine.Tests.Infrastructure;

namespace Muwbta.Engine.Tests.Commands;

/// <summary>
/// With a word list configured, every way of talking refuses a listed word before it reaches
/// anyone; without one, nothing changes.
/// </summary>
/// <remarks>
/// The same five verbs the mute covers, for the same reason: a filter with a way around it is a
/// filter in name only. The matching itself is tested on <see cref="WordFilter"/>; this is about
/// the gate being on every door.
/// </remarks>
public sealed class LanguageGateTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");
    private static readonly RoomKey East = RoomKey.Parse("test.zone.east");

    private static WorldHarness Filtered()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        harness.Options.BlockedWords = "blort\nzarg";
        return harness;
    }

    [Theory]
    [InlineData("say what a blort")]
    [InlineData("'blort")]
    [InlineData("emote mutters blort")]
    [InlineData(";blort")]
    [InlineData("chat blort to all")]
    [InlineData("tell Kael you blort")]
    public void A_listed_word_is_refused_on_every_channel(string input)
    {
        var harness = Filtered();
        var speaker = harness.AddPlayer("Bram", West);
        var neighbour = harness.AddPlayer("Vurn", West);
        var far = harness.AddPlayer("Kael", East);
        harness.Drain(speaker);
        harness.Drain(neighbour);
        harness.Drain(far);

        harness.Execute(speaker, input);

        Assert.Contains("not allowed here", harness.DrainText(speaker), StringComparison.Ordinal);
        Assert.DoesNotContain("blort", harness.DrainText(neighbour), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("blort", harness.DrainText(far), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Party_chat_is_covered_too()
    {
        var harness = Filtered();
        var leader = harness.AddPlayer("Bram", West);
        var member = harness.AddPlayer("Kael", West);

        harness.Execute(leader, "group invite Kael");
        harness.Execute(member, "group join");
        harness.Drain(leader);
        harness.Drain(member);

        harness.Execute(leader, "gtell zarg");

        Assert.Contains("not allowed here", harness.DrainText(leader), StringComparison.Ordinal);
        Assert.DoesNotContain("zarg", harness.DrainText(member), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Everything_else_is_said_as_before()
    {
        var harness = Filtered();
        var speaker = harness.AddPlayer("Bram", West);
        var neighbour = harness.AddPlayer("Vurn", West);
        harness.Drain(neighbour);

        harness.Execute(speaker, "say the blorting is fine, and so is Scunthorpe");

        Assert.Contains("Scunthorpe", harness.DrainText(neighbour), StringComparison.Ordinal);
    }

    [Fact]
    public void With_no_list_nothing_is_refused()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var speaker = harness.AddPlayer("Bram", West);
        var neighbour = harness.AddPlayer("Vurn", West);
        harness.Drain(neighbour);

        harness.Execute(speaker, "say blort");

        Assert.Contains("blort", harness.DrainText(neighbour), StringComparison.Ordinal);
    }

    [Fact]
    public void The_list_takes_effect_without_a_restart()
    {
        // It arrives the way the welcome message does: the applier sets it on the options the
        // loop is already holding.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var speaker = harness.AddPlayer("Bram", West);
        var neighbour = harness.AddPlayer("Vurn", West);

        harness.Options.BlockedWords = "zarg";
        harness.Drain(neighbour);

        harness.Execute(speaker, "say zarg");

        Assert.DoesNotContain("zarg", harness.DrainText(neighbour), StringComparison.Ordinal);
    }
}
