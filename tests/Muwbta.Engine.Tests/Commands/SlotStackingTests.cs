using Muwbta.Domain.Combat;
using Muwbta.Domain.Items;
using Muwbta.Domain.Worlds;
using Muwbta.Engine.Tests.Infrastructure;

namespace Muwbta.Engine.Tests.Commands;

/// <summary>
/// One slot holds one thing, whichever verb put it there (BUGS.md #8, #9).
/// </summary>
/// <remarks>
/// <para>
/// <c>EquippedSlot</c> is the only record of what a character is wearing, and
/// <see cref="EquipmentResolver"/> sums armour across every item carrying one with no per-slot
/// dedup — so the occupied-slot check in <c>wear</c> is not a convenience, it is the whole
/// invariant. It scans <c>InventoryOf</c>, which is keyed on ownership, so any verb that changes
/// ownership without clearing the slot opens the invariant.
/// </para>
/// <para>
/// Three did. <c>drop</c>, <c>give</c> and <c>sell</c> all moved an item out of a pack and left
/// <c>EquippedSlot</c> set, so dropping a worn ring, wearing a second, and picking the first back
/// up left both reporting the same slot and both counting toward armour. Repeat for as many as you
/// own.
/// </para>
/// <para>
/// Fixed at both levels on purpose: the state primitives clear the slot so the resolver can never
/// see two items in one, and the commands refuse outright so the player is told rather than quietly
/// undressed. Either alone would leave the other half of the bug — refusing in the commands still
/// lets a builder-spawned or imported instance carry a stale slot, and clearing in the primitives
/// alone would silently unequip whatever you sold.
/// </para>
/// </remarks>
public sealed class SlotStackingTests
{
    private static readonly RoomKey Room = RoomKey.Parse("test.zone.west");

    private static WorldHarness Loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return harness;
    }

    private static ItemTemplate Ring(WorldHarness harness, string key) =>
        harness.AddItemTemplate(new ItemTemplate
        {
            Key = key,
            Name = $"a {key}",
            Icon = "=",
            Slots = [ItemSlot.Trinket],
            BaseStats = new Dictionary<string, object> { ["armor"] = 10 },
        });

    /// <summary>
    /// A shopkeeper stocking nothing, authored the way the database delivers the bag — a decimal
    /// or a list out of jsonb arrives as a <c>JsonElement</c>, which is the trap
    /// <see cref="WorldHarness.AsPersisted"/> exists for.
    /// </summary>
    private static void AddShopkeeper(WorldHarness harness) =>
        harness.AddMob("barkeep", Room, name: "barkeep", behavior: WorldHarness.AsPersisted(
            new Dictionary<string, object>
            {
                ["shopkeeper"] = true,
                ["type"] = "npc",
                ["sells"] = new List<object>(),
            }));

    private static int ArmorOf(WorldHarness harness, Muwbta.Engine.World.PlayerActor actor) =>
        EquipmentResolver.ResolveDefenderStats(
            actor.Character.Level,
            actor.Character.Attributes.AgilityModifier,
            harness.World.EquipmentOf(actor.CharacterId)).Armor;

    // -----------------------------------------------------------------------
    // The exploit, end to end
    // -----------------------------------------------------------------------

    /// <summary>
    /// Drop a worn ring, wear a second, pick the first up: one trinket slot, one ring's armour.
    /// </summary>
    [Fact]
    public void Dropping_a_worn_item_and_picking_it_back_up_does_not_stack_the_slot()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        harness.GiveItem(kael, Ring(harness, "first-ring"));
        harness.GiveItem(kael, Ring(harness, "second-ring"));

        // No `remove` anywhere in here. That is the exploit: dropping it while worn is what leaves
        // the slot set, and picking it up is what brings the slot back with it.
        harness.Execute(kael, "wear first-ring");
        harness.Execute(kael, "drop first-ring");
        harness.Execute(kael, "wear second-ring");
        harness.Execute(kael, "get first-ring");

        var equipped = harness.World.EquipmentOf(kael.CharacterId);

        Assert.Single(equipped);
        Assert.Equal(10, ArmorOf(harness, kael));
    }

    // -----------------------------------------------------------------------
    // The primitives, which are the half no command guard can reach
    // -----------------------------------------------------------------------

    /// <summary>
    /// Leaving a pack takes the item off, whoever asked and by whatever route.
    /// </summary>
    /// <remarks>
    /// Driven against <see cref="World.WorldState"/> rather than through a verb on purpose. The
    /// commands refuse an equipped item outright, so this state is unreachable from a player's
    /// keyboard — but it is entirely reachable from a builder spawn, an import, or a row written
    /// before this rule existed, and the resolver must never see two items in one slot however the
    /// second one got there.
    /// </remarks>
    [Fact]
    public void The_drop_primitive_clears_the_slot()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var ring = harness.GiveItem(kael, Ring(harness, "first-ring"));
        harness.World.EquipItem(ring, ItemSlot.Trinket);

        harness.World.DropItem(ring, Room);

        Assert.Null(ring.EquippedSlot);
    }

    /// <summary>
    /// And arriving in one does too, so a stale slot on a loose item cannot be picked up.
    /// </summary>
    /// <remarks>
    /// <c>TryEquip</c> is the only place <see cref="ItemRules.RefusePath"/> is consulted, so an item
    /// that arrives already wearing its slot has bypassed the Path restriction entirely. Today's
    /// content survives that by coincidence — every Path-restricted item is also no-drop — and a
    /// coincidence of authoring is not a rule.
    /// </remarks>
    [Fact]
    public void The_pickup_primitive_clears_the_slot()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var mira = harness.AddPlayer("Mira", Room);

        var ring = harness.GiveItem(kael, Ring(harness, "first-ring"));
        harness.World.EquipItem(ring, ItemSlot.Trinket);

        harness.World.PickUpItem(ring, mira.CharacterId);

        Assert.Equal(mira.CharacterId, ring.OwnerCharacterId);
        Assert.Null(ring.EquippedSlot);
    }

    // -----------------------------------------------------------------------
    // What the player is told
    // -----------------------------------------------------------------------

    [Fact]
    public void Dropping_something_you_are_wearing_says_so()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        harness.GiveItem(kael, Ring(harness, "first-ring"));

        harness.Execute(kael, "wear first-ring");
        harness.Drain(kael);
        harness.Execute(kael, "drop first-ring");

        Assert.Contains("have to remove", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void Giving_something_you_are_wearing_says_so()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        harness.AddPlayer("Mira", Room);
        harness.GiveItem(kael, Ring(harness, "first-ring"));

        harness.Execute(kael, "wear first-ring");
        harness.Drain(kael);
        harness.Execute(kael, "give first-ring Mira");

        Assert.Contains("have to remove", harness.DrainText(kael), StringComparison.Ordinal);
    }

    /// <summary>
    /// You cannot sell the sword in your hand. <c>destroy</c> has always refused this.
    /// </summary>
    [Fact]
    public void Selling_something_you_are_wearing_is_refused()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        AddShopkeeper(harness);
        var ring = harness.GiveItem(kael, Ring(harness, "first-ring"));

        harness.Execute(kael, "wear first-ring");
        harness.Drain(kael);
        harness.Execute(kael, "sell first-ring");

        Assert.Equal(ItemSlot.Trinket, ring.EquippedSlot);
        Assert.Contains("have to remove", harness.DrainText(kael), StringComparison.Ordinal);
    }

    /// <summary>
    /// A no-drop item cannot be sold either. <c>drop</c> tells the player that destroying it is the
    /// only sanctioned way to be rid of it; a shop that bought it would be an unsanctioned way that
    /// also paid.
    /// </summary>
    [Fact]
    public void Selling_a_no_drop_item_is_refused()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        AddShopkeeper(harness);

        var bound = harness.AddItemTemplate(new ItemTemplate
        {
            Key = "bound-ring",
            Name = "a bound ring",
            Icon = "=",
            Slots = [ItemSlot.Trinket],
            IsNoDrop = true,
            BaseValue = 100,
        });

        harness.GiveItem(kael, bound);
        harness.Drain(kael);
        harness.Execute(kael, "sell bound-ring");

        Assert.Single(harness.World.InventoryOf(kael.CharacterId));
        Assert.Contains("will not leave your hand", harness.DrainText(kael), StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // What must keep working
    // -----------------------------------------------------------------------

    [Fact]
    public void An_unequipped_item_still_drops_normally()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        harness.GiveItem(kael, Ring(harness, "first-ring"));

        harness.Execute(kael, "drop first-ring");

        Assert.Empty(harness.World.InventoryOf(kael.CharacterId));
        Assert.Single(harness.World.ItemsIn(Room));
    }

    [Fact]
    public void Removing_then_dropping_is_still_two_steps_that_work()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var ring = harness.GiveItem(kael, Ring(harness, "first-ring"));

        harness.Execute(kael, "wear first-ring");
        harness.Execute(kael, "remove first-ring");
        harness.Execute(kael, "drop first-ring");

        Assert.Null(ring.EquippedSlot);
        Assert.Single(harness.World.ItemsIn(Room));
    }
}
