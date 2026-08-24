using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Items;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Systems;
using DikuWeb.Engine.Tests.Infrastructure;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Tests.Systems;

/// <summary>
/// Mob loot nobody wanted leaves the world; everything else stays where it was put.
/// </summary>
/// <remarks>
/// <para>
/// Nothing swept the floor at all before this. An item put in a room stayed there for the life of
/// the process, so a farming spot accumulated every drop nobody bothered with until a restart, and
/// a restart was the only thing that ever cleared it.
/// </para>
/// <para>
/// <b>The distinction is where the stamp is written, not what the sweep knows.</b> Only
/// <c>RollLoot</c> stamps, so <see cref="GroundDecay.HasExpired"/> is false for a player's drop, a
/// builder's placement and a spawner's population without the sweep having to recognise any of
/// them. These tests exist to hold that line: a sweep that learned to tell them apart by some
/// other means would pass the first test here and fail the rest.
/// </para>
/// </remarks>
public sealed class GroundDecayTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");

    private static Dictionary<string, object> Always(string itemKey) =>
        new(StringComparer.Ordinal) { ["itemTemplateKey"] = itemKey, ["chance"] = 1.0 };

    private static (WorldHarness Harness, ItemTemplate Fang) WithFang()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();

        var fang = harness.AddItemTemplate(new ItemTemplate
        {
            Key = "fang",
            Name = "a tarnished fang",
            Description = "Dropped.",
            Icon = "$",
        });

        return (harness, fang);
    }

    /// <summary>A killer who has just put a fang on the floor by killing something.</summary>
    private static (WorldHarness Harness, PlayerActor Killer) AfterAKill()
    {
        var (harness, _) = WithFang();

        var killer = harness.AddPlayer("Bram", West, path: CharacterPath.Warden, level: 10);
        harness.AddMob("rat", West, health: 1, name: "large rat", loot: [Always("fang")]);

        harness.Execute(killer, "kill rat");
        harness.Pump(32);

        Assert.True(OnTheFloor(harness), "the kill should have dropped a fang");

        return (harness, killer);
    }

    private static bool OnTheFloor(WorldHarness harness) =>
        harness.World.ItemsIn(West).Any(i => i.DisplayName == "a tarnished fang");

    /// <summary>Runs the sweep at the clock's current time, the way the loop does once a minute.</summary>
    private static void Sweep(WorldHarness harness) =>
        GroundDecaySystem.Tick(harness.World, harness.Clock.UtcNow, harness.View, harness.ItemSaves);

    // -----------------------------------------------------------------------
    // Mob loot
    // -----------------------------------------------------------------------

    [Fact]
    public void Untaken_loot_is_gone_after_its_twenty_minutes()
    {
        var (harness, _) = AfterAKill();

        harness.Clock.Advance(GroundDecay.Lifetime + TimeSpan.FromSeconds(1));
        Sweep(harness);

        Assert.False(OnTheFloor(harness));
    }

    /// <summary>
    /// A party that clears a room, sits down to recover and comes back for the pile should still
    /// find it. The lifetime is generous on purpose — nothing is racing this.
    /// </summary>
    [Fact]
    public void Loot_is_still_there_a_minute_before_the_deadline()
    {
        var (harness, _) = AfterAKill();

        harness.Clock.Advance(GroundDecay.Lifetime - TimeSpan.FromMinutes(1));
        Sweep(harness);

        Assert.True(OnTheFloor(harness));
    }

    /// <summary>
    /// Said out loud. An item vanishing from the room listing with nothing to explain it reads as
    /// the client having lost track, and the thing people do about that is reload.
    /// </summary>
    [Fact]
    public void The_room_is_told_what_happened()
    {
        var (harness, killer) = AfterAKill();

        harness.Drain(killer);
        harness.Clock.Advance(GroundDecay.Lifetime + TimeSpan.FromSeconds(1));
        Sweep(harness);

        Assert.Contains(
            "A tarnished fang crumbles away.", harness.DrainText(killer), StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // Everything else
    // -----------------------------------------------------------------------

    /// <summary>
    /// A player's drop is on no clock at all. It stays until somebody takes it or the server
    /// restarts, which is the rule this whole system is carved out of.
    /// </summary>
    [Fact]
    public void An_item_a_player_dropped_never_decays()
    {
        var (harness, fang) = WithFang();
        var actor = harness.AddPlayer("Kaeda", West, path: CharacterPath.Temper, level: 10);

        harness.GiveItem(actor, fang);
        harness.Execute(actor, "drop fang");
        Assert.True(OnTheFloor(harness));

        // Far past any deadline the loot path would have set.
        harness.Clock.Advance(GroundDecay.Lifetime * 3);
        Sweep(harness);

        Assert.True(OnTheFloor(harness));
    }

    /// <summary>
    /// A room item that was never loot — a builder's placement, a spawner's population — is left
    /// alone for the same reason: nothing stamped it.
    /// </summary>
    [Fact]
    public void An_item_that_was_never_loot_never_decays()
    {
        var (harness, fang) = WithFang();

        harness.DropItemInRoom(fang, West);

        harness.Clock.Advance(GroundDecay.Lifetime * 3);
        Sweep(harness);

        Assert.True(OnTheFloor(harness));
    }

    /// <summary>
    /// <b>Picking it up ends the countdown for good.</b> Left on the instance, a stamp already in
    /// the past would delete the item the moment it touched the floor again — so a player who
    /// looted something, carried it across the zone and put it down would watch it evaporate.
    /// </summary>
    [Fact]
    public void Taking_it_and_dropping_it_puts_it_back_on_no_clock()
    {
        var (harness, killer) = AfterAKill();

        harness.Execute(killer, "get fang");
        harness.Clock.Advance(GroundDecay.Lifetime + TimeSpan.FromMinutes(5));
        harness.Execute(killer, "drop fang");

        Assert.True(OnTheFloor(harness), "dropping it should not have destroyed it");

        Sweep(harness);

        Assert.True(OnTheFloor(harness));
    }

    // -----------------------------------------------------------------------
    // The database
    // -----------------------------------------------------------------------

    /// <summary>
    /// <b>Dropping deletes the row rather than writing one.</b> Nothing ever reads a room item
    /// back — login loads only what a character owns — so a saved drop never survived a restart
    /// anyway; it only left a row behind for ever, every time anybody put anything down.
    /// </summary>
    [Fact]
    public void Dropping_an_item_takes_its_row_out_of_the_database()
    {
        var (harness, fang) = WithFang();
        var actor = harness.AddPlayer("Kaeda", West, path: CharacterPath.Temper, level: 10);

        var item = harness.GiveItem(actor, fang);

        harness.ItemSaves.Saved.Clear();
        harness.ItemSaves.Deleted.Clear();

        harness.Execute(actor, "drop fang");

        Assert.Contains(item.Id, harness.ItemSaves.Deleted);
        Assert.DoesNotContain(harness.ItemSaves.Saved, i => i.Id == item.Id);
    }

    /// <summary>
    /// And picking it back up writes it again, which is what makes the delete safe: the row is
    /// re-created for whoever ends up owning the thing.
    /// </summary>
    [Fact]
    public void Picking_it_up_again_writes_the_row_back()
    {
        var (harness, fang) = WithFang();
        var actor = harness.AddPlayer("Kaeda", West, path: CharacterPath.Temper, level: 10);

        var item = harness.DropItemInRoom(fang, West);

        harness.ItemSaves.Saved.Clear();
        harness.Execute(actor, "get fang");

        Assert.Contains(harness.ItemSaves.Saved, i => i.Id == item.Id);
        Assert.Equal(actor.CharacterId, item.OwnerCharacterId);
    }
}
