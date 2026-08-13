using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Tests.Infrastructure;

namespace DikuWeb.Engine.Tests.Commands;

/// <summary>
/// Shops, from the shape the behavior bag actually arrives in (PLAN.md §5.2c).
/// </summary>
/// <remarks>
/// Every behavior here is authored through <see cref="WorldHarness.AsPersisted"/>, because the
/// bug these cover was invisible to a hand-built bag: <c>shopkeeper</c> and <c>sells</c> come
/// back from jsonb as <c>JsonElement</c>, and the old <c>is bool</c> / <c>is List&lt;object&gt;</c>
/// checks were false for every one of them. The shop worked in tests and nowhere else.
/// </remarks>
public sealed class ShopCommandTests
{
    private static readonly RoomKey Room = RoomKey.Parse("test.zone.west");

    private static WorldHarness Loaded()
    {
        var harness = new WorldHarness();
        harness.LoadTestWorld();
        return harness;
    }

    /// <summary>A shopkeeper stocking one item, authored the way the database delivers it.</summary>
    private static void AddShopkeeper(WorldHarness harness, params string[] sells) =>
        AddShopkeeper(harness, markup: null, sells);

    /// <summary>The same, priced over base value (PLAN.md §4.13).</summary>
    /// <remarks>
    /// The markup goes through <see cref="WorldHarness.AsPersisted"/> like the rest of the bag,
    /// because a decimal out of jsonb arrives as a <c>JsonElement</c> - the same trap that had
    /// killed shopkeeper detection, the stock list, and idle emotes.
    /// </remarks>
    private static void AddShopkeeper(WorldHarness harness, decimal? markup, params string[] sells)
    {
        var behavior = new Dictionary<string, object>
        {
            ["shopkeeper"] = true,
            ["type"] = "npc",
            ["sells"] = new List<object>(sells),
        };

        if (markup is not null)
        {
            behavior["markup"] = markup.Value;
        }

        harness.AddMob("barkeep", Room, name: "barkeep", behavior: WorldHarness.AsPersisted(behavior));
    }

    [Fact]
    public void A_shopkeeper_loaded_from_storage_is_recognised_as_one()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        harness.DefineItem("bread", "loaf of bread", slot: null, value: 5);
        AddShopkeeper(harness, "bread");
        harness.Drain(kael);

        harness.Execute(kael, "list");

        var text = harness.DrainText(kael);
        Assert.DoesNotContain("no shopkeeper", text, StringComparison.Ordinal);
        Assert.Contains("loaf of bread", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_shop_list_prices_each_item_it_stocks()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        harness.DefineItem("bread", "loaf of bread", slot: null, value: 5);
        harness.DefineItem("torch", "pitch torch", slot: null, value: 12);
        AddShopkeeper(harness, "bread", "torch");
        harness.Drain(kael);

        harness.Execute(kael, "list");

        var text = harness.DrainText(kael);
        Assert.Contains("loaf of bread: 5 gold", text, StringComparison.Ordinal);
        Assert.Contains("pitch torch: 12 gold", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Buying_moves_the_item_into_the_pack_and_takes_the_gold()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        kael.Character.Gold = 50;
        harness.DefineItem("bread", "loaf of bread", slot: null, value: 5);
        AddShopkeeper(harness, "bread");

        harness.Execute(kael, "buy bread");

        Assert.Equal(45, kael.Character.Gold);
        Assert.Contains(
            harness.World.InventoryOf(kael.CharacterId),
            i => i.TemplateKey == "bread");
    }

    [Fact]
    public void Buying_what_the_shop_does_not_stock_is_refused()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        kael.Character.Gold = 50;
        harness.DefineItem("bread", "loaf of bread", slot: null, value: 5);
        harness.DefineItem("crown", "gold crown", slot: null, value: 900);
        AddShopkeeper(harness, "bread");
        harness.Drain(kael);

        harness.Execute(kael, "buy crown");

        Assert.Equal(50, kael.Character.Gold);
        Assert.Empty(harness.World.InventoryOf(kael.CharacterId));
        Assert.Contains("doesn't have that", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void Buying_without_the_gold_is_refused()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        kael.Character.Gold = 2;
        harness.DefineItem("bread", "loaf of bread", slot: null, value: 5);
        AddShopkeeper(harness, "bread");
        harness.Drain(kael);

        harness.Execute(kael, "buy bread");

        Assert.Equal(2, kael.Character.Gold);
        Assert.Empty(harness.World.InventoryOf(kael.CharacterId));
        Assert.Contains("You need 5 gold", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void Selling_credits_the_sellback_share_and_deletes_the_row()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var bread = harness.DefineItem("bread", "loaf of bread", slot: null, value: 10);
        var carried = harness.GiveItem(kael, bread);
        AddShopkeeper(harness, "bread");

        harness.Execute(kael, "sell bread");

        // Default sellback is half, and the delete has to reach storage or the loaf comes back.
        Assert.Equal(5, kael.Character.Gold);
        Assert.Empty(harness.World.InventoryOf(kael.CharacterId));
        Assert.Equal([carried.Id], harness.ItemSaves.Deleted);
    }

    [Fact]
    public void A_quest_item_loaded_from_storage_still_cannot_be_sold()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var letter = harness.DefineItem("sealed-letter", "sealed letter", slot: null, value: 40);

        // The flag through the same round trip the rest of the bag takes. Read as a raw `is bool`
        // this is false, and the shop buys the letter - the rule fails open, which is the worst
        // way for a guard to break.
        var carried = harness.GiveItem(
            kael,
            letter,
            WorldHarness.AsPersisted(new Dictionary<string, object> { ["questItem"] = true }));
        AddShopkeeper(harness, "bread");
        harness.Drain(kael);

        harness.Execute(kael, "sell letter");

        Assert.Equal(0, kael.Character.Gold);
        Assert.Contains(carried, harness.World.InventoryOf(kael.CharacterId));
        Assert.Empty(harness.ItemSaves.Deleted);
        Assert.Contains("refuses to buy a quest item", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void A_shop_stocking_a_deleted_template_says_so_rather_than_listing_short()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        AddShopkeeper(harness, "ghost-item");
        harness.Drain(kael);

        harness.Execute(kael, "list");

        Assert.Contains("ghost-item", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void A_mob_that_is_not_a_shopkeeper_does_not_run_a_shop()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        harness.AddMob("rat", Room, name: "rat");
        harness.Drain(kael);

        harness.Execute(kael, "list");

        Assert.Contains("no shopkeeper", harness.DrainText(kael), StringComparison.Ordinal);
    }

    /// <summary>
    /// Two shops in one room - a market square. Each verb has to cope with more than one.
    /// </summary>
    private static void AddTwoShops(WorldHarness harness)
    {
        harness.DefineItem("anvil", "iron anvil", slot: null, value: 80);
        harness.DefineItem("bread", "loaf of bread", slot: null, value: 5);

        harness.AddMob(
            "smith",
            Room,
            name: "smith",
            behavior: WorldHarness.AsPersisted(new Dictionary<string, object>
            {
                ["shopkeeper"] = true,
                ["sells"] = new List<object> { "anvil" },
            }));

        harness.AddMob(
            "baker",
            Room,
            name: "baker",
            behavior: WorldHarness.AsPersisted(new Dictionary<string, object>
            {
                ["shopkeeper"] = true,
                ["sells"] = new List<object> { "bread" },
            }));
    }

    [Fact]
    public void List_shows_every_shop_in_the_room()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        AddTwoShops(harness);
        harness.Drain(kael);

        harness.Execute(kael, "list");

        var text = harness.DrainText(kael);
        Assert.Contains("iron anvil", text, StringComparison.Ordinal);
        Assert.Contains("loaf of bread", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Buying_reaches_the_shop_that_stocks_the_item_not_merely_the_first_one()
    {
        // The baker is second in the room. Binding every verb to the first shopkeeper made the
        // bread unbuyable while the baker stood right there.
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        kael.Character.Gold = 100;
        AddTwoShops(harness);
        harness.Drain(kael);

        harness.Execute(kael, "buy bread");

        Assert.Equal(95, kael.Character.Gold);
        Assert.Contains(harness.World.InventoryOf(kael.CharacterId), i => i.TemplateKey == "bread");
    }

    [Fact]
    public void Buying_from_the_first_shop_still_works_when_a_second_is_present()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        kael.Character.Gold = 100;
        AddTwoShops(harness);
        harness.Drain(kael);

        harness.Execute(kael, "buy anvil");

        Assert.Equal(20, kael.Character.Gold);
        Assert.Contains(harness.World.InventoryOf(kael.CharacterId), i => i.TemplateKey == "anvil");
    }

    [Fact]
    public void An_item_no_shop_in_the_room_stocks_is_refused()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        kael.Character.Gold = 100;
        AddTwoShops(harness);
        harness.DefineItem("crown", "gold crown", slot: null, value: 900);
        harness.Drain(kael);

        harness.Execute(kael, "buy crown");

        Assert.Equal(100, kael.Character.Gold);
        // Naming one shop would be misleading when the player cannot tell which one answered.
        Assert.Contains("None of the shopkeepers here", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void Selling_works_with_more_than_one_shop_present()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        AddTwoShops(harness);
        var bread = harness.DefineItem("bread", "loaf of bread", slot: null, value: 10);
        harness.GiveItem(kael, bread);

        harness.Execute(kael, "sell bread");

        Assert.Equal(5, kael.Character.Gold);
        Assert.Empty(harness.World.InventoryOf(kael.CharacterId));
    }

    [Fact]
    public void A_marked_up_shop_lists_the_raised_price()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        harness.DefineItem("bread", "loaf of bread", slot: null, value: 1);
        harness.DefineItem("torch", "pitch torch", slot: null, value: 10);
        AddShopkeeper(harness, markup: 0.1m, "bread", "torch");
        harness.Drain(kael);

        harness.Execute(kael, "list");

        // Rounded up, so the tenth is visible on the loaf rather than vanishing into it.
        var text = harness.DrainText(kael);
        Assert.Contains("loaf of bread: 2 gold", text, StringComparison.Ordinal);
        Assert.Contains("pitch torch: 11 gold", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Buying_charges_what_the_list_quoted()
    {
        // The load-bearing property of the markup: one number reaches the player twice. A `list`
        // and a `buy` that disagreed would be a bug the player pays for.
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        kael.Character.Gold = 50;
        harness.DefineItem("torch", "pitch torch", slot: null, value: 10);
        AddShopkeeper(harness, markup: 0.25m, "torch");
        harness.Drain(kael);

        harness.Execute(kael, "list");
        Assert.Contains("pitch torch: 13 gold", harness.DrainText(kael), StringComparison.Ordinal);

        harness.Execute(kael, "buy torch");

        Assert.Equal(37, kael.Character.Gold);
        Assert.Contains(harness.World.InventoryOf(kael.CharacterId), i => i.TemplateKey == "torch");
    }

    [Fact]
    public void The_refusal_quotes_the_marked_up_price_too()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        kael.Character.Gold = 11;
        harness.DefineItem("torch", "pitch torch", slot: null, value: 10);
        AddShopkeeper(harness, markup: 0.25m, "torch");
        harness.Drain(kael);

        harness.Execute(kael, "buy torch");

        // Affordable at base value, not at this shop's. Quoting the base here would tell a player
        // they had enough while refusing them.
        Assert.Equal(11, kael.Character.Gold);
        Assert.Contains("You need 13 gold", harness.DrainText(kael), StringComparison.Ordinal);
    }

    [Fact]
    public void A_markup_does_not_move_what_the_shop_pays()
    {
        // §4.13: markup is a buy-side dial. "Expensive to buy from" and "pays well" have to stay
        // two things a builder sets independently.
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        var bread = harness.DefineItem("bread", "loaf of bread", slot: null, value: 10);
        harness.GiveItem(kael, bread);
        AddShopkeeper(harness, markup: 1.0m, "bread");

        harness.Execute(kael, "sell bread");

        Assert.Equal(5, kael.Character.Gold);
    }

    [Fact]
    public void An_unmarked_shop_still_charges_base_value()
    {
        // Absence is the neutral value, as it is for every other key in the bag.
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        kael.Character.Gold = 50;
        harness.DefineItem("bread", "loaf of bread", slot: null, value: 5);
        AddShopkeeper(harness, markup: null, "bread");

        harness.Execute(kael, "buy bread");

        Assert.Equal(45, kael.Character.Gold);
    }

    [Fact]
    public void Each_shop_in_a_room_prices_its_own_stock()
    {
        // Two traders, one dear and one not. Pricing from the first shopkeeper in the room would
        // put the smith's markup on the baker's bread.
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        kael.Character.Gold = 100;
        harness.DefineItem("anvil", "iron anvil", slot: null, value: 20);
        harness.DefineItem("bread", "loaf of bread", slot: null, value: 10);

        harness.AddMob(
            "smith",
            Room,
            name: "smith",
            behavior: WorldHarness.AsPersisted(new Dictionary<string, object>
            {
                ["shopkeeper"] = true,
                ["sells"] = new List<object> { "anvil" },
                ["markup"] = 0.5m,
            }));

        harness.AddMob(
            "baker",
            Room,
            name: "baker",
            behavior: WorldHarness.AsPersisted(new Dictionary<string, object>
            {
                ["shopkeeper"] = true,
                ["sells"] = new List<object> { "bread" },
            }));

        harness.Drain(kael);

        harness.Execute(kael, "buy bread");

        Assert.Equal(90, kael.Character.Gold);
    }

    [Fact]
    public void A_shopkeeper_with_an_empty_stock_says_so()
    {
        var harness = Loaded();
        var kael = harness.AddPlayer("Kael", Room);
        AddShopkeeper(harness);
        harness.Drain(kael);

        harness.Execute(kael, "list");

        Assert.Contains("nothing to sell", harness.DrainText(kael), StringComparison.Ordinal);
    }
}
