using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Protocol;
using DikuWeb.Engine.Tests.Infrastructure;

namespace DikuWeb.Engine.Tests.Presentation;

/// <summary>
/// A room is redrawn once per pulse, however many times it changed during it.
/// </summary>
/// <remarks>
/// <para>
/// The last of three quadratics on the room-refresh path, each removed in turn: the layout was
/// built once per viewer, then the payload was copied once per viewer, and then — with both of
/// those gone — the number of <em>refreshes</em> still rose with the number of people moving while
/// the recipients of each rose with how many were standing there. Twenty people walking through
/// one room in a single tick meant twenty rebuilds and twenty broadcasts of a room whose final
/// state was the only one anybody could perceive.
/// </para>
/// <para>
/// Measured at two hundred sessions crowded into three rooms, the three changes together took the
/// time spent inside pulse handlers from 63% of the window to a fraction of it, and stopped the
/// loop missing three pulses in ten (PLAN.md §11).
/// </para>
/// </remarks>
public sealed class RoomRefreshCoalescingTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");
    private static readonly RoomKey East = RoomKey.Parse("test.zone.east");

    [Fact]
    public void Marking_one_room_many_times_sends_it_once()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var kael = harness.AddPlayer("Kael", West);
        harness.Drain(kael);

        for (var i = 0; i < 20; i++)
        {
            harness.View.MarkRoomChanged(West);
        }

        harness.View.FlushChangedRooms(harness.World);

        var events = harness.Drain(kael);

        Assert.Equal(1, events.Count(e => e.Type == EventTypes.Map));
        Assert.Equal(1, events.Count(e => e.Type == EventTypes.Contents));
    }

    [Fact]
    public void Marking_is_not_sending()
    {
        // The whole point: a handler that marks a room has sent nothing yet. If this ever starts
        // failing, refreshes have gone back to being immediate and the coalescing is dead.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var kael = harness.AddPlayer("Kael", West);
        harness.Drain(kael);

        harness.View.MarkRoomChanged(West);

        Assert.Empty(harness.Drain(kael));
    }

    [Fact]
    public void Each_room_that_changed_is_still_sent()
    {
        // Deduplication must not become "only the first one". A movement marks two rooms and both
        // have to arrive, or the room somebody walked out of would keep showing them standing in
        // it until something else happened there.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var kael = harness.AddPlayer("Kael", West);
        var mira = harness.AddPlayer("Mira", East);
        harness.Drain(kael);
        harness.Drain(mira);

        harness.View.MarkRoomChanged(West);
        harness.View.MarkRoomChanged(East);
        harness.View.MarkRoomChanged(West);

        harness.View.FlushChangedRooms(harness.World);

        Assert.Equal(1, harness.Drain(kael).Count(e => e.Type == EventTypes.Map));
        Assert.Equal(1, harness.Drain(mira).Count(e => e.Type == EventTypes.Map));
    }

    [Fact]
    public void The_set_is_emptied_by_flushing()
    {
        // A room stays marked until it is sent, and then stops being marked. A set that never
        // cleared would redraw every room anybody had ever visited, on every pulse, forever.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var kael = harness.AddPlayer("Kael", West);
        harness.Drain(kael);

        harness.View.MarkRoomChanged(West);
        harness.View.FlushChangedRooms(harness.World);
        harness.Drain(kael);

        harness.View.FlushChangedRooms(harness.World);

        Assert.Empty(harness.Drain(kael));
    }

    [Fact]
    public void Walking_marks_both_rooms_and_sends_each_once()
    {
        // The real path, through a command rather than through the mark directly. WorldHarness
        // flushes after every command, exactly as the loop flushes at the end of every pulse.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var kael = harness.AddPlayer("Kael", West);
        var watcher = harness.AddPlayer("Mira", West);
        harness.Drain(kael);
        harness.Drain(watcher);

        harness.Execute(kael, "east");

        // The watcher stayed behind, so everything they were sent is the refresh of the room they
        // are still standing in - one map, not one per thing that happened in the room.
        var seen = harness.Drain(watcher);
        Assert.Equal(1, seen.Count(e => e.Type == EventTypes.Map));
    }
}
