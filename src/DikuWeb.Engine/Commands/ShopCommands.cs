using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Narration;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Inhabitants;
using DikuWeb.Engine.Spawning;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Commands;

/// <summary>
/// Buying and selling (PLAN.md §5.2c).
/// </summary>
/// <remarks>
/// The caches and options come from the <see cref="CommandContext"/> rather than from statics
/// captured at registration. They were static, which made the last-constructed registry the one
/// every other registry read from - harmless in the server, which builds one, and a race in the
/// tests, which build one per case and run classes in parallel.
/// </remarks>
public static class ShopCommands
{
    public static void Register(List<CommandDefinition> commands)
    {
        commands.Add(new CommandDefinition(
            "list", 3, "list - see what a shopkeeper has", ListShop));

        commands.Add(new CommandDefinition(
            "buy", 1, "buy <item> - purchase an item from a shopkeeper", Buy));

        commands.Add(new CommandDefinition(
            "sell", 1, "sell <item> - sell an item to a shopkeeper", Sell));
    }

    /// <summary>
    /// Whether the shop system is wired up and its templates are in hand.
    /// </summary>
    /// <remarks>
    /// All three verbs ask the same question. Only <c>list</c> used to, so before the caches had
    /// loaded a player was told the shop did not exist by one verb and served by the other two.
    /// </remarks>
    private static bool IsShopAvailable(CommandContext ctx) =>
        ctx.MobTemplates is { IsLoaded: true } && ctx.ItemTemplates is not null;

    /// <summary>
    /// Every shopkeeper standing in this room, in the order they are stood in it.
    /// </summary>
    /// <remarks>
    /// A list rather than a single shopkeeper, because a market square is an obvious thing to
    /// build and nothing stops a builder putting two spawners in one room. Binding the verbs to
    /// the first match made the second shop unreachable: there is no syntax for naming a
    /// shopkeeper, so "buy bread" beside a smith and a baker refused the bread. Worse, the match
    /// shifted as mobs wandered in and out, so which shop a room *had* was not stable.
    /// </remarks>
    private static List<MobTemplate> ShopkeepersIn(CommandContext ctx)
    {
        if (ctx.MobTemplates is null)
        {
            return [];
        }

        return [.. ctx.World.MobsIn(ctx.Actor.Character.RoomKey)
            .Select(mob => ctx.MobTemplates.Get(mob.TemplateKey))
            .Where(template => MobBehavior.IsShopkeeper(template?.Behavior))
            .OfType<MobTemplate>()];
    }

    private static void ListShop(CommandContext ctx)
    {
        if (!IsShopAvailable(ctx))
        {
            ctx.Reply("The shop is not available.");
            return;
        }

        var shopkeepers = ShopkeepersIn(ctx);
        if (shopkeepers.Count == 0)
        {
            ctx.Reply("There is no shopkeeper here.");
            return;
        }

        // Every shop, not just the first. A player standing in a market square can see there are
        // two traders in front of them; a listing that covered one of them would read as a bug.
        foreach (var template in shopkeepers)
        {
            var stock = MobBehavior.SellsOf(template.Behavior);
            if (stock.Count == 0)
            {
                ctx.Reply($"{template.Name} has nothing to sell.");
                continue;
            }

            ctx.Reply($"=== {template.Name}'s Shop ===");

            foreach (var itemKey in stock)
            {
                var itemTemplate = ctx.ItemTemplates?.Get(itemKey);
                if (itemTemplate is null)
                {
                    // The shop lists a template that no longer exists. Saying so beats a short
                    // list the builder cannot account for - the shop equivalent of a dangling exit.
                    ctx.Reply($"  (an empty space where {itemKey} should be)", "dim");
                    continue;
                }

                var price = (long)itemTemplate.BaseValue;
                ctx.Reply($"{itemTemplate.Icon} {itemTemplate.Name}: {price} gold");
            }
        }
    }

    private static void Buy(CommandContext ctx)
    {
        if (!ctx.HasArgument)
        {
            ctx.Reply("Buy what?");
            return;
        }

        if (!IsShopAvailable(ctx))
        {
            ctx.Reply("The shop is not available.");
            return;
        }

        var character = ctx.Actor.Character;
        var itemName = ctx.Argument;

        var shopkeepers = ShopkeepersIn(ctx);
        if (shopkeepers.Count == 0)
        {
            ctx.Reply("There is no shopkeeper here.");
            return;
        }

        // Ask every shop in the room, not just the first. The player named an item, not a
        // trader - and there is no syntax for naming one - so "buy bread" has to find the baker
        // even when the smith happens to be standing nearer the door.
        var (shopkeeperTemplate, itemKey) = shopkeepers
            .Select(shop => (
                Shop: shop,
                // Matched on the stocked item's display name as well as its key, so "buy bread"
                // works against a "loaf-of-bread" key the player has never seen.
                Key: NameMatch.Best(
                    MobBehavior.SellsOf(shop.Behavior),
                    itemName,
                    k => ctx.ItemTemplates?.Get(k)?.Name,
                    k => k)))
            .FirstOrDefault(match => match.Key is not null);

        if (itemKey is null)
        {
            // "Nothing here sells that" rather than naming one shop, which would be misleading
            // when several are present and the player cannot tell which one answered.
            ctx.Reply(
                shopkeepers.Count == 1
                    ? $"{shopkeepers[0].Name} doesn't have that."
                    : "None of the shopkeepers here has that.");
            return;
        }

        var itemTemplate = ctx.ItemTemplates?.Get(itemKey);
        if (itemTemplate is null)
        {
            ctx.Reply("The shopkeeper doesn't have that.");
            return;
        }

        var price = (long)itemTemplate.BaseValue;
        if (character.Gold < price)
        {
            ctx.Reply($"You need {price} gold, but only have {character.Gold}.");
            return;
        }

        // Spawn the item. The lookup wants the qualified "world.zone" key - RoomKey.Zone is the
        // bare segment, so this always missed and every purchase died on the guard below
        // reporting the shop temporarily unavailable.
        var zone = ctx.World.FindZone(character.RoomKey.ZoneKey);
        var world = zone is not null ? ctx.World.FindWorld(zone.WorldKey) : null;

        if (zone is null || world is null)
        {
            ctx.Reply("The shop is temporarily unavailable.");
            return;
        }

        // The spawner's instance is used directly. This used to spawn one and then hand-build a
        // second from its parts, which was identical field for field - and would now silently
        // drop the questItem stamp the spawner applies.
        var instance = new ItemSpawner().Spawn(itemTemplate, zone, world, character.RoomKey);

        ctx.World.AddItem(instance);
        ctx.World.PickUpItem(instance, character.Id);
        ctx.ItemSaveQueue?.Enqueue(instance);

        // Deduct gold
        character.Gold -= price;

        ctx.Reply($"You buy {NarrationHelper.WithArticle(itemTemplate.Name)} for {price} gold.", "success");
        ctx.Broadcast(
            $"{character.Name} buys something from {NarrationHelper.WithArticle(shopkeeperTemplate.Name)}.",
            "activity");
    }

    private static void Sell(CommandContext ctx)
    {
        if (!ctx.HasArgument)
        {
            ctx.Reply("Sell what?");
            return;
        }

        if (!IsShopAvailable(ctx))
        {
            ctx.Reply("The shop is not available.");
            return;
        }

        var character = ctx.Actor.Character;
        var itemName = ctx.Argument;

        // Any shopkeeper will take it: sellback is a flat share of the item's value, so which
        // one buys it is not observable. Stock lists are what a shop sells, not what it accepts.
        var shopkeeperTemplate = ShopkeepersIn(ctx).FirstOrDefault();
        if (shopkeeperTemplate is null)
        {
            ctx.Reply("There is no shopkeeper here.");
            return;
        }

        // Find the item in inventory
        var inventory = ctx.World.InventoryOf(character.Id);
        var itemToSell = NameMatch.Best(
            inventory, itemName, i => i.TemplateName, i => i.TemplateKey);

        if (itemToSell is null)
        {
            ctx.Reply("You don't have that.");
            return;
        }

        if (ItemState.IsQuestItem(itemToSell))
        {
            ctx.Reply(
                $"{NarrationHelper.WithArticle(shopkeeperTemplate.Name, capitalize: true)} refuses to buy a quest item.");
            return;
        }

        // Calculate sellback price
        var sellbackPercent = ctx.Options?.ShopSellbackPercent ?? 0.5m;
        var sellPrice = (long)(itemToSell.Value * sellbackPercent);

        // Remove item and credit gold. The delete has to reach storage too, or the row
        // outlives the sale still pointing at its former owner and the item comes back in
        // the player's inventory on restart.
        ctx.World.RemoveItem(itemToSell);
        ctx.ItemSaveQueue?.EnqueueDelete(itemToSell.Id);
        character.Gold += sellPrice;

        ctx.Reply(
            $"You sell {NarrationHelper.WithDefiniteArticle(itemToSell.TemplateName)} for {sellPrice} gold.",
            "success");
        ctx.Broadcast(
            $"{character.Name} sells something to {NarrationHelper.WithArticle(shopkeeperTemplate.Name)}.",
            "activity");
    }
}
