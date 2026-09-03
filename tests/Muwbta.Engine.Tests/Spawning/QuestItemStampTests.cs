using Muwbta.Domain.Items;
using Muwbta.Domain.Worlds;
using Muwbta.Engine;
using Muwbta.Engine.Spawning;
using Muwbta.Engine.Tests.Infrastructure;

namespace Muwbta.Engine.Tests.Spawning;

/// <summary>
/// The <c>questItem</c> flag, from the template that declares it to the instance that carries it
/// (PLAN.md §4.9: cannot be sold or destroyed, can still be dropped).
/// </summary>
/// <remarks>
/// Both readers were already correct and both were dead: nothing in the game ever wrote the flag,
/// so the shop happily bought quest items and `destroy` happily destroyed them. The write lives in
/// <see cref="ItemSpawner"/> because that is the one path every spawn goes through - shop stock,
/// quest rewards, the spawner sweep, and combat loot.
/// </remarks>
public sealed class QuestItemStampTests
{
    private static readonly RoomKey Room = RoomKey.Parse("test.zone.west");

    private static (Zone Zone, Domain.Worlds.World World) Scope()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return (harness.Zone, harness.World_);
    }

    private static ItemTemplate Template(bool isQuestItem) => new()
    {
        Key = "sealed-letter",
        Name = "sealed letter",
        Icon = "?",
        IsQuestItem = isQuestItem,
    };

    [Fact]
    public void A_quest_item_template_stamps_its_instances()
    {
        var (zone, world) = Scope();

        var instance = new ItemSpawner().Spawn(Template(isQuestItem: true), zone, world, Room);

        Assert.True(ItemState.IsQuestItem(instance));
    }

    [Fact]
    public void An_ordinary_template_does_not()
    {
        var (zone, world) = Scope();

        var instance = new ItemSpawner().Spawn(Template(isQuestItem: false), zone, world, Room);

        Assert.False(ItemState.IsQuestItem(instance));
        Assert.Empty(instance.State);
    }

    [Fact]
    public void Each_copy_is_stamped_independently()
    {
        // A quest that rewards three of something must not hand out one flagged copy and two
        // free ones - or, worse, three aliases of the same state dictionary.
        var (zone, world) = Scope();
        var spawner = new ItemSpawner();
        var template = Template(isQuestItem: true);

        var first = spawner.Spawn(template, zone, world, Room);
        var second = spawner.Spawn(template, zone, world, Room);

        Assert.True(ItemState.IsQuestItem(first));
        Assert.True(ItemState.IsQuestItem(second));
        Assert.NotSame(first.State, second.State);
    }

    [Fact]
    public void A_bought_quest_item_arrives_flagged_and_cannot_be_sold_back()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var kael = harness.AddPlayer("Kael", Room);
        kael.Character.Gold = 100;

        // Declared on the template, exactly as the builder would.
        var letter = harness.DefineItem("sealed-letter", "sealed letter", slot: null, value: 20);
        letter.IsQuestItem = true;

        harness.AddMob(
            "barkeep",
            Room,
            name: "barkeep",
            behavior: WorldHarness.AsPersisted(new Dictionary<string, object>
            {
                ["shopkeeper"] = true,
                ["sells"] = new List<object> { "sealed-letter" },
            }));

        harness.Execute(kael, "buy sealed-letter");
        harness.Drain(kael);
        harness.Execute(kael, "sell letter");

        Assert.Equal(80, kael.Character.Gold);
        Assert.Contains("refuses to buy a quest item", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void A_quest_item_cannot_be_destroyed_once_it_exists_in_the_world()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var kael = harness.AddPlayer("Kael", Room);

        var letter = harness.DefineItem("sealed-letter", "sealed letter", slot: null);
        letter.IsQuestItem = true;

        var instance = new ItemSpawner().Spawn(letter, harness.Zone, harness.World_, Room);
        harness.World.AddItem(instance);
        harness.World.PickUpItem(instance, kael.CharacterId);
        harness.Drain(kael);

        harness.Execute(kael, "destroy sealed-letter");

        Assert.Contains(instance, harness.World.InventoryOf(kael.CharacterId));
        Assert.Empty(harness.ItemSaves.Deleted);
        Assert.Contains("bound to a quest", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void A_quest_item_can_still_be_dropped()
    {
        // The other half of the §4.9 rule, and the reason "cannot be destroyed" is not simply
        // "cannot leave your pack".
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        var kael = harness.AddPlayer("Kael", Room);

        var letter = harness.DefineItem("sealed-letter", "sealed letter", slot: null);
        letter.IsQuestItem = true;

        var instance = new ItemSpawner().Spawn(letter, harness.Zone, harness.World_, Room);
        harness.World.AddItem(instance);
        harness.World.PickUpItem(instance, kael.CharacterId);

        harness.Execute(kael, "drop sealed-letter");

        Assert.Contains(instance, harness.World.ItemsIn(Room));
        Assert.Empty(harness.World.InventoryOf(kael.CharacterId));
    }
}
