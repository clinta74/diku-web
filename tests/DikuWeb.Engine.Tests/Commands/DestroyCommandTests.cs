using DikuWeb.Domain.Items;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Tests.Infrastructure;

namespace DikuWeb.Engine.Tests.Commands;

/// <summary>
/// Destroy is the one inventory verb with no way back, so its guards matter more than its
/// happy path: what it refuses, and whether the removal reaches storage rather than only the
/// in-memory world.
/// </summary>
public sealed class DestroyCommandTests
{
    private static readonly RoomKey Room = RoomKey.Parse("test.zone.west");

    private static WorldHarness Loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return harness;
    }

    [Fact]
    public void Destroying_a_carried_item_removes_it_from_the_inventory()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var junk = harness.DefineItem("old-coin", "old coin", slot: null);
        harness.GiveItem(kael, junk);

        harness.Execute(kael, "destroy old-coin");

        Assert.Empty(harness.World.InventoryOf(kael.CharacterId));
    }

    [Fact]
    public void A_destroyed_item_is_deleted_from_storage_too()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var junk = harness.DefineItem("old-coin", "old coin", slot: null);
        var item = harness.GiveItem(kael, junk);

        harness.Execute(kael, "destroy old-coin");

        // Without this the row outlives the destruction and the item returns on the next load.
        Assert.Equal([item.Id], harness.ItemSaves.Deleted);
    }

    [Fact]
    public void A_destroyed_item_does_not_fall_to_the_floor()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var junk = harness.DefineItem("old-coin", "old coin", slot: null);
        harness.GiveItem(kael, junk);

        harness.Execute(kael, "destroy old-coin");

        Assert.Empty(harness.World.ItemsIn(Room));
    }

    [Fact]
    public void An_equipped_item_is_refused_and_the_player_is_pointed_at_remove()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var helm = harness.DefineItem("iron-helm", "iron helm", ItemSlot.Head);
        var worn = harness.Equip(kael, helm, ItemSlot.Head);
        harness.Drain(kael);

        harness.Execute(kael, "destroy iron-helm");

        Assert.Contains(worn, harness.World.InventoryOf(kael.CharacterId));
        Assert.Empty(harness.ItemSaves.Deleted);
        Assert.Contains("remove", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void A_quest_item_cannot_be_destroyed()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var token = harness.DefineItem("sealed-letter", "sealed letter", slot: null);
        var item = harness.GiveItem(kael, token);
        item.State["questItem"] = true;
        harness.Drain(kael);

        harness.Execute(kael, "destroy sealed-letter");

        Assert.Contains(item, harness.World.InventoryOf(kael.CharacterId));
        Assert.Empty(harness.ItemSaves.Deleted);
        Assert.Contains("quest", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void Destroying_something_you_are_not_carrying_says_so()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        harness.Drain(kael);

        harness.Execute(kael, "destroy old-coin");

        Assert.Empty(harness.ItemSaves.Deleted);
        Assert.Contains("don't have", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void An_item_on_the_floor_is_out_of_reach()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var junk = harness.DefineItem("old-coin", "old coin", slot: null);
        var item = harness.GiveItem(kael, junk);
        harness.World.DropItem(item, Room);
        harness.Drain(kael);

        harness.Execute(kael, "destroy old-coin");

        // Destroy reads the inventory only. Reaching the floor would let a player delete
        // something another player had just put down.
        Assert.Contains(item, harness.World.ItemsIn(Room));
        Assert.Empty(harness.ItemSaves.Deleted);
    }

    [Fact]
    public void Destroy_with_no_argument_asks_what()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        harness.Drain(kael);

        harness.Execute(kael, "destroy");

        Assert.Contains("Destroy what?", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void Others_in_the_room_see_the_item_destroyed()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var mira = harness.AddPlayer("Mira", Room);
        var junk = harness.DefineItem("old-coin", "old coin", slot: null);
        harness.GiveItem(kael, junk);
        harness.Drain(mira);

        harness.Execute(kael, "destroy old-coin");

        Assert.Contains("Kael destroys the old coin", harness.DrainText(mira), StringComparison.Ordinal);
    }

    /// <summary>
    /// The verb demands all seven characters. A prefix that reached it would put an unrecoverable
    /// command one keystroke from "d", which the direction table hands to down.
    /// </summary>
    [Theory]
    [InlineData("d")]
    [InlineData("de")]
    [InlineData("destro")]
    public void Destroy_cannot_be_abbreviated(string prefix)
    {
        var harness = Loaded();

        Assert.NotEqual("destroy", harness.Commands.Find(prefix)?.Name);
    }
}
