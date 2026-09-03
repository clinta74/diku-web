using Muwbta.Domain.Worlds;
using Muwbta.Engine.Tests.Infrastructure;
using Muwbta.Engine.World;

namespace Muwbta.Engine.Tests.Systems;

/// <summary>
/// Waking up somewhere else (PLAN.md §4.12).
/// </summary>
/// <remarks>
/// Reported from play as <em>"You died. Respawned at ossara.gatetown.the-gate-yard."</em> — a room
/// key in player prose, the same defect as the innkeeper's before it. Reading the code around that
/// line turned up the larger half: death was the one relocation in the game that never showed the
/// player where they had ended up. Walking, recall, portal and <c>goto</c> all send the room and
/// redraw both ends; this moved the character and said a key, leaving the description, the exits
/// and the map all belonging to the room they had just died in.
/// </remarks>
public sealed class PlayerDeathTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");
    private static readonly RoomKey East = RoomKey.Parse("test.zone.east");

    /// <summary>
    /// Kills <paramref name="player"/> where they stand, and returns everything they were told.
    /// </summary>
    private static string Die(WorldHarness harness, PlayerActor player)
    {
        var killer = harness.AddMob("bear", East, health: 500, damageMin: 40, damageMax: 40);
        killer.ResolvedStats["defense"] = -100;

        player.Character.Vitals.Health = 1;
        harness.Execute(player, "attack bear");
        harness.Drain(player);
        harness.Pump(40);

        return harness.DrainText(player);
    }

    [Fact]
    public void The_death_line_names_the_room_rather_than_its_key()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var kael = harness.AddPlayer("Kael", East);

        var text = Die(harness, kael);

        Assert.Contains("You wake in The west room.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("test.zone.west", text, StringComparison.Ordinal);
    }

    [Fact]
    public void You_are_shown_the_room_you_wake_in()
    {
        // The description belongs only to the prose the room event carries, so its presence is
        // what says the room was actually sent rather than merely named.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var kael = harness.AddPlayer("Kael", East);

        var text = Die(harness, kael);

        Assert.Equal(West, kael.RoomKey);
        Assert.Contains("A featureless west room used for testing.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_room_you_died_in_is_told_you_fell()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var kael = harness.AddPlayer("Kael", East);
        var ilse = harness.AddPlayer("Ilse", East);
        harness.Drain(ilse);

        Die(harness, kael);

        Assert.Contains("Kael falls.", harness.DrainText(ilse), StringComparison.Ordinal);
    }

    [Fact]
    public void The_room_you_wake_in_is_told_you_arrived()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var kael = harness.AddPlayer("Kael", East);
        var bram = harness.AddPlayer("Bram", West);
        harness.Drain(bram);

        Die(harness, kael);

        Assert.Contains("Kael appears.", harness.DrainText(bram), StringComparison.Ordinal);
    }
}
