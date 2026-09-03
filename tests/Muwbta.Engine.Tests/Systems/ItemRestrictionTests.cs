using Muwbta.Domain.Characters;
using Muwbta.Domain.Items;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Tests.Infrastructure;

namespace Muwbta.Engine.Tests.Systems;

/// <summary>
/// The three restrictions an item template can carry: lore, no-drop, and Path.
/// </summary>
/// <remarks>
/// Every test here is about a <em>route</em> rather than about the flag. A restriction enforced on
/// three of the four ways an item can reach a pack is not a restriction, and which routes exist is
/// the part that is easy to be wrong about.
/// </remarks>
public sealed class ItemRestrictionTests
{
    private static readonly RoomKey West = RoomKey.Parse("test.zone.west");

    private static ItemTemplate Template(
        string key,
        bool lore = false,
        bool noDrop = false,
        ItemSlot? slot = ItemSlot.MainHand,
        params CharacterPath[] paths) => new()
        {
            Key = key,
            Name = key.Replace('-', ' '),
            Icon = "/",
            Slots = slot is null ? [] : [slot.Value],
            BaseValue = 10,
            IsLore = lore,
            IsNoDrop = noDrop,
            Paths = [.. paths],
        };

    // -----------------------------------------------------------------------
    // Lore: one only, counting what is worn
    // -----------------------------------------------------------------------

    [Fact]
    public void A_second_lore_item_cannot_be_picked_up()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var temper = harness.AddItemTemplate(Template("oath-temper", lore: true));

        var player = harness.AddPlayer("Kael", West);
        harness.GiveItem(player, temper);
        harness.DropItemInRoom(temper, West);
        harness.Drain(player);

        harness.Execute(player, "get oath");

        Assert.Single(harness.World.InventoryOf(player.CharacterId), i => i.TemplateKey == "oath-temper");
        Assert.Contains("one is all", harness.DrainText(player), StringComparison.Ordinal);
    }

    [Fact]
    public void The_one_you_are_wielding_counts()
    {
        // The loophole the flag exists to close: one in the pack and one in each hand is three.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var temper = harness.AddItemTemplate(Template("oath-temper", lore: true));

        var player = harness.AddPlayer("Kael", West);
        var held = harness.GiveItem(player, temper);
        harness.World.EquipItem(held, ItemSlot.MainHand);

        harness.DropItemInRoom(temper, West);
        harness.Drain(player);

        harness.Execute(player, "get oath");

        Assert.Single(harness.World.InventoryOf(player.CharacterId), i => i.TemplateKey == "oath-temper");
    }

    [Fact]
    public void A_lore_item_cannot_be_given_to_somebody_who_has_one()
    {
        // Asked on the receiving side. Asked on the giver's, two players hand one back and forth
        // and each ends the exchange holding a copy they could not have picked up.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var temper = harness.AddItemTemplate(Template("oath-temper", lore: true));

        var giver = harness.AddPlayer("Kael", West);
        var taker = harness.AddPlayer("Ilse", West);

        harness.GiveItem(giver, temper);
        harness.GiveItem(taker, temper);
        harness.Drain(giver);

        harness.Execute(giver, "give oath Ilse");

        Assert.Single(harness.World.InventoryOf(taker.CharacterId), i => i.TemplateKey == "oath-temper");
        Assert.Contains("already has one", harness.DrainText(giver), StringComparison.Ordinal);
    }

    [Fact]
    public void A_lore_item_can_be_given_to_somebody_who_has_none()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var temper = harness.AddItemTemplate(Template("oath-temper", lore: true));

        var giver = harness.AddPlayer("Kael", West);
        var taker = harness.AddPlayer("Ilse", West);
        harness.GiveItem(giver, temper);

        harness.Execute(giver, "give oath Ilse");

        Assert.Single(harness.World.InventoryOf(taker.CharacterId), i => i.TemplateKey == "oath-temper");
    }

    // -----------------------------------------------------------------------
    // No-drop: bound, but destroyable
    // -----------------------------------------------------------------------

    [Fact]
    public void A_no_drop_item_will_not_be_put_down()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var temper = harness.AddItemTemplate(Template("oath-temper", noDrop: true));

        var player = harness.AddPlayer("Kael", West);
        harness.GiveItem(player, temper);
        harness.Drain(player);

        harness.Execute(player, "drop oath");

        Assert.Single(harness.World.InventoryOf(player.CharacterId));
        Assert.Empty(harness.World.ItemsIn(West));
    }

    [Fact]
    public void A_no_drop_item_will_not_be_handed_over()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var temper = harness.AddItemTemplate(Template("oath-temper", noDrop: true));

        var giver = harness.AddPlayer("Kael", West);
        var taker = harness.AddPlayer("Ilse", West);
        harness.GiveItem(giver, temper);
        harness.Drain(giver);

        harness.Execute(giver, "give oath Ilse");

        Assert.Empty(harness.World.InventoryOf(taker.CharacterId));
        Assert.Contains("will not go to anyone else", harness.DrainText(giver), StringComparison.Ordinal);
    }

    [Fact]
    public void The_refusal_says_destroy_is_the_way_out()
    {
        // A bound item with no stated way to be rid of it is a pack slot the player cannot reason
        // about. Destroy is deliberately still allowed.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var temper = harness.AddItemTemplate(Template("oath-temper", noDrop: true));

        var player = harness.AddPlayer("Kael", West);
        harness.GiveItem(player, temper);
        harness.Drain(player);

        harness.Execute(player, "drop oath");

        Assert.Contains("destroy", harness.DrainText(player), StringComparison.OrdinalIgnoreCase);
    }

    // -----------------------------------------------------------------------
    // Path: who may equip it
    // -----------------------------------------------------------------------

    [Fact]
    public void A_path_locked_item_refuses_the_wrong_path()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var oath = harness.AddItemTemplate(Template("oath-temper", paths: CharacterPath.Warden));

        var adept = harness.AddPlayer("Ilse", West, path: CharacterPath.Adept);
        var worn = harness.GiveItem(adept, oath);
        harness.Drain(adept);

        harness.Execute(adept, "wield oath");

        Assert.Null(worn.EquippedSlot);
        Assert.Contains("only a Warden", harness.DrainText(adept), StringComparison.Ordinal);
    }

    [Fact]
    public void A_path_locked_item_accepts_a_listed_path()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var oath = harness.AddItemTemplate(
            Template("oath-temper", false, false, ItemSlot.MainHand, CharacterPath.Warden, CharacterPath.Hallow));

        var hallow = harness.AddPlayer("Bram", West, path: CharacterPath.Hallow);
        var worn = harness.GiveItem(hallow, oath);

        harness.Execute(hallow, "wield oath");

        Assert.Equal(ItemSlot.MainHand, worn.EquippedSlot);
    }

    [Fact]
    public void An_unrestricted_item_is_equippable_by_anybody()
    {
        // The default, and it has to be the default: an empty list means no restriction, so an
        // authored item is unrestricted until a builder opts in.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var plain = harness.AddItemTemplate(Template("plain-temper"));

        var adept = harness.AddPlayer("Ilse", West, path: CharacterPath.Adept);
        var worn = harness.GiveItem(adept, plain);

        harness.Execute(adept, "wield plain");

        Assert.Equal(ItemSlot.MainHand, worn.EquippedSlot);
    }

    [Fact]
    public void A_path_locked_item_can_still_be_carried_and_handed_over()
    {
        // Deliberate. A Temper should be able to pick a Warden's temper off the floor and take it to
        // the Warden - the restriction is on equipping, not on touching.
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var oath = harness.AddItemTemplate(Template("oath-temper", paths: CharacterPath.Warden));

        var temper = harness.AddPlayer("Ilse", West, path: CharacterPath.Temper);
        var warden = harness.AddPlayer("Kael", West, path: CharacterPath.Warden);

        harness.DropItemInRoom(oath, West);
        harness.Execute(temper, "get oath");
        Assert.Single(harness.World.InventoryOf(temper.CharacterId));

        harness.Execute(temper, "give oath Kael");
        Assert.Single(harness.World.InventoryOf(warden.CharacterId));
    }
}
