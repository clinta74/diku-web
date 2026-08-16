using DikuWeb.Domain.Items;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Tests.Infrastructure;

namespace DikuWeb.Engine.Tests.Commands;

/// <summary>
/// Wearing and wielding must honour the item's declared slot. The original handlers hardcoded
/// Chest for every worn item and never checked whether an item was equippable at all, so a
/// sword or a ring both ended up on the chest.
/// </summary>
public sealed class EquipCommandTests
{
    private static readonly RoomKey Room = RoomKey.Parse("test.zone.west");

    private static WorldHarness Loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return harness;
    }

    [Fact]
    public void Wielding_a_weapon_puts_it_in_the_main_hand_not_the_chest()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var sword = harness.DefineItem("rusted-blade", "a rusted blade", ItemSlot.MainHand);
        var item = harness.GiveItem(kael, sword);

        harness.Execute(kael, "wield rusted-blade");

        Assert.Equal(ItemSlot.MainHand, item.EquippedSlot);
    }

    [Fact]
    public void Wearing_a_trinket_puts_it_in_the_trinket_slot_not_the_chest()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var ring = harness.DefineItem("gold-ring", "a simple gold ring", ItemSlot.Trinket);
        var item = harness.GiveItem(kael, ring);

        harness.Execute(kael, "wear gold-ring");

        Assert.Equal(ItemSlot.Trinket, item.EquippedSlot);
    }

    [Fact]
    public void A_weapon_cannot_be_worn_and_the_player_is_pointed_at_wield()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var sword = harness.DefineItem("rusted-blade", "a rusted blade", ItemSlot.MainHand);
        var item = harness.GiveItem(kael, sword);
        harness.Drain(kael);

        harness.Execute(kael, "wear rusted-blade");

        Assert.Null(item.EquippedSlot);
        Assert.Contains("wielding", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void A_trinket_cannot_be_wielded_and_the_player_is_pointed_at_wear()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var ring = harness.DefineItem("gold-ring", "a simple gold ring", ItemSlot.Trinket);
        var item = harness.GiveItem(kael, ring);
        harness.Drain(kael);

        harness.Execute(kael, "wield gold-ring");

        Assert.Null(item.EquippedSlot);
        Assert.Contains("wearing", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void An_item_with_no_slot_cannot_be_worn_at_all()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var junk = harness.DefineItem("old-coin", "an old coin", slot: null);
        var item = harness.GiveItem(kael, junk);
        harness.Drain(kael);

        harness.Execute(kael, "wear old-coin");

        Assert.Null(item.EquippedSlot);
        Assert.Contains("can't wear", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void A_second_item_cannot_take_an_already_occupied_slot()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var helm = harness.DefineItem("iron-helm", "an iron helm", ItemSlot.Head);
        var cap = harness.DefineItem("leather-cap", "a leather cap", ItemSlot.Head);
        var wornHelm = harness.GiveItem(kael, helm);
        var wornCap = harness.GiveItem(kael, cap);

        harness.Execute(kael, "wear iron-helm");
        harness.Drain(kael);
        harness.Execute(kael, "wear leather-cap");

        Assert.Equal(ItemSlot.Head, wornHelm.EquippedSlot);
        Assert.Null(wornCap.EquippedSlot);
        Assert.Contains("already using", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void Examining_an_item_describes_how_it_can_be_used()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var ring = harness.DefineItem(
            "gold-ring", "a simple gold ring", ItemSlot.Trinket, "A plain band of soft gold.");
        harness.GiveItem(kael, ring);
        harness.Drain(kael);

        harness.Execute(kael, "examine gold-ring");
        var text = harness.DrainText(kael);

        Assert.Contains("A plain band of soft gold.", text, StringComparison.Ordinal);
        Assert.Contains("trinket", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Examining_a_weapon_says_it_can_be_wielded()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var sword = harness.DefineItem("rusted-blade", "a rusted blade", ItemSlot.MainHand);
        harness.GiveItem(kael, sword);
        harness.Drain(kael);

        harness.Execute(kael, "examine rusted-blade");

        Assert.Contains("wield", harness.DrainText(kael), StringComparison.Ordinal);
    }

    /// <summary>
    /// A torch, a shield, anything with no declared attack delay never swings — so wielding one
    /// must not blame the training, which is a fix that does not exist for it.
    /// </summary>
    /// <remarks>
    /// Found through the light sources (PLAN.md §4.18): the pitch torch takes the off hand, so
    /// every player who buys one meets this line. The `stats` screen has always said the right
    /// thing one screen over — <em>"Not a weapon — it never strikes."</em>
    /// </remarks>
    [Fact]
    public void Wielding_something_that_is_not_a_weapon_says_nothing_about_training()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var torch = harness.DefineItem("pitch-torch", "a pitch torch", ItemSlot.OffHand);
        var item = harness.GiveItem(kael, torch);
        harness.Drain(kael);

        harness.Execute(kael, "wield pitch-torch");

        Assert.Equal(ItemSlot.OffHand, item.EquippedSlot);
        Assert.DoesNotContain("training", harness.DrainText(kael), StringComparison.Ordinal);
    }

    /// <summary>A second weapon still warns, because there the training is the missing thing.</summary>
    [Fact]
    public void Wielding_a_second_weapon_untrained_still_says_so()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var dagger = harness.DefineItem("off-dagger", "a short dagger", ItemSlot.OffHand);
        dagger.AttackDelayPulses = 6;
        harness.GiveItem(kael, dagger);
        harness.Drain(kael);

        harness.Execute(kael, "wield off-dagger");

        Assert.Contains("training", harness.DrainText(kael), StringComparison.Ordinal);
    }
}
