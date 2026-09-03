using Muwbta.Domain.Worlds;
using Muwbta.Engine.Protocol;
using Muwbta.Engine.Tests.Infrastructure;

namespace Muwbta.Engine.Tests.Commands;

public sealed class CommandBehaviourTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");
    private static readonly RoomKey Middle = RoomKey.Parse("test.zone.middle");
    private static readonly RoomKey East = RoomKey.Parse("test.zone.east");

    private static WorldHarness Loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return harness;
    }

    [Fact]
    public void An_emote_is_shown_to_the_person_who_wrote_it()
    {
        // It used to broadcast only, so `;grins` produced nothing on your own screen — and in an
        // empty room there was no way to tell a working emote from a swallowed one.
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", West);
        harness.Drain(kael);

        harness.Execute(kael, ";grins slowly");

        Assert.Contains("Kael grins slowly", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void An_emote_reads_the_same_way_to_everyone()
    {
        // The same third-person line rather than a second-person rewrite: "grins" → "grin" is
        // easy, and there is no rule that also handles "waves at the fire".
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", West);
        var mira = harness.AddPlayer("Mira", West);
        harness.Drain(kael);
        harness.Drain(mira);

        harness.Execute(kael, "emote waves at the fire");

        Assert.Equal(
            harness.DrainText(mira).Trim(),
            harness.DrainText(kael).Trim());
    }

    [Fact]
    public void Moving_relocates_the_character_and_updates_occupancy()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", West);

        harness.Execute(kael, "east");

        Assert.Equal(Middle, kael.RoomKey);
        Assert.Empty(harness.World.OccupantsOf(West));
        Assert.Single(harness.World.OccupantsOf(Middle));
    }

    [Fact]
    public void Moving_tells_both_rooms_what_happened()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", West);
        var mira = harness.AddPlayer("Mira", West);
        var tarn = harness.AddPlayer("Tarn", Middle);

        harness.Drain(mira);
        harness.Drain(tarn);

        harness.Execute(kael, "east");

        Assert.Contains("Kael leaves east", harness.DrainText(mira), StringComparison.Ordinal);
        Assert.Contains("Kael arrives from the west", harness.DrainText(tarn), StringComparison.Ordinal);
    }

    [Fact]
    public void Moving_into_a_wall_leaves_the_character_where_it_was()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", West);
        harness.Drain(kael);

        harness.Execute(kael, "west");

        Assert.Equal(West, kael.RoomKey);
        Assert.Contains("You cannot go west", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void A_dangling_exit_fails_closed_instead_of_throwing()
    {
        // Live editing with no publish gate means an exit can point at a room that does not
        // exist yet (PLAN.md §7.4). This must never take down the game loop.
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", East);
        harness.Drain(kael);

        harness.Execute(kael, "north");

        Assert.Equal(East, kael.RoomKey);
        Assert.Contains("The way is blocked", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void Say_reaches_the_room_but_not_other_rooms()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", West);
        var mira = harness.AddPlayer("Mira", West);
        var tarn = harness.AddPlayer("Tarn", East);

        harness.Drain(mira);
        harness.Drain(tarn);

        harness.Execute(kael, "say watch the gate");

        Assert.Contains("Kael says, 'watch the gate'", harness.DrainText(mira), StringComparison.Ordinal);
        Assert.DoesNotContain("watch the gate", harness.DrainText(tarn), StringComparison.Ordinal);
    }

    [Fact]
    public void Say_echoes_to_the_speaker()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", West);
        harness.Drain(kael);

        harness.Execute(kael, "say hello");

        Assert.Contains("You say, 'hello'", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void Say_with_nothing_to_say_is_rejected()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", West);
        harness.Drain(kael);

        harness.Execute(kael, "say");

        Assert.Contains("Say what?", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void Look_sends_the_room_map_and_contents_panels()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", West);
        harness.Drain(kael);

        harness.Execute(kael, "look");
        var events = harness.Drain(kael);

        Assert.Contains(events, e => e.Type == EventTypes.Room);
        Assert.Contains(events, e => e.Type == EventTypes.Map);
        Assert.Contains(events, e => e.Type == EventTypes.Contents);
        Assert.Contains(events, e => e.Type == EventTypes.Text);
    }

    [Fact]
    public void Look_includes_the_description_but_moving_does_not()
    {
        // Classic MUD brevity: the full description on look, just the title and exits on
        // every step, or walking six rooms buries the scrollback.
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", West);

        harness.Drain(kael);
        harness.Execute(kael, "look");
        Assert.Contains("featureless west room", harness.DrainText(kael), StringComparison.Ordinal);

        harness.Execute(kael, "east");
        Assert.DoesNotContain("featureless middle room", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void Look_lists_other_occupants()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", West);
        harness.AddPlayer("Mira", West);
        harness.Drain(kael);

        harness.Execute(kael, "look");

        Assert.Contains("Mira is here", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void Who_lists_players_across_every_room()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", West);
        harness.AddPlayer("Mira", East);
        harness.Drain(kael);

        harness.Execute(kael, "who");
        var text = harness.DrainText(kael);

        Assert.Contains("2 players online", text, StringComparison.Ordinal);
        Assert.Contains("Kael", text, StringComparison.Ordinal);
        Assert.Contains("Mira", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Quit_asks_the_loop_to_remove_the_player()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", West);

        var context = harness.Execute(kael, "quit");

        Assert.Equal(LeaveReason.Quit, context.LeaveRequested);
    }

    [Fact]
    public void Help_lists_the_non_direction_commands()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", West);
        harness.Drain(kael);

        harness.Execute(kael, "help");
        var text = harness.DrainText(kael);

        Assert.Contains("look", text, StringComparison.Ordinal);
        Assert.Contains("say", text, StringComparison.Ordinal);
        Assert.Contains("quit", text, StringComparison.Ordinal);
    }
}
