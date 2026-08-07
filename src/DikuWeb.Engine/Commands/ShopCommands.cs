using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Narration;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Inhabitants;
using DikuWeb.Engine.Spawning;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Commands;

public static class ShopCommands
{
    private static MobTemplateCache? _mobTemplateCache;
    private static ItemTemplateCache? _itemTemplateCache;
    private static EngineOptions? _options;

    public static void Register(
        List<CommandDefinition> commands,
        MobTemplateCache? mobTemplateCache,
        ItemTemplateCache? itemTemplateCache,
        EngineOptions? options = null)
    {
        _mobTemplateCache = mobTemplateCache;
        _itemTemplateCache = itemTemplateCache;
        _options = options;

        commands.Add(new CommandDefinition(
            "list", 3, "list - see what a shopkeeper has", ListShop));

        commands.Add(new CommandDefinition(
            "buy", 1, "buy <item> - purchase an item from a shopkeeper", Buy));

        commands.Add(new CommandDefinition(
            "sell", 1, "sell <item> - sell an item to a shopkeeper", Sell));
    }

    private static void ListShop(CommandContext ctx)
    {
        if (_mobTemplateCache is null || !_mobTemplateCache.IsLoaded)
        {
            ctx.Reply("The shop is not available.");
            return;
        }

        var character = ctx.Actor.Character;

        // Find shopkeeper in current room
        var shopkeeper = ctx.World.MobsIn(character.RoomKey)
            .FirstOrDefault(m =>
            {
                var template = _mobTemplateCache.Get(m.TemplateKey);
                return template?.Behavior.TryGetValue("shopkeeper", out var isShop) == true &&
                       isShop is bool b && b;
            });

        if (shopkeeper is null)
        {
            ctx.Reply("There is no shopkeeper here.");
            return;
        }

        var template = _mobTemplateCache.Get(shopkeeper.TemplateKey);
        if (template?.Behavior.TryGetValue("sells", out var sellsList) != true || sellsList is not List<object> items)
        {
            ctx.Reply($"{template?.Name ?? "The shopkeeper"} has nothing to sell.");
            return;
        }

        ctx.Reply($"=== {template.Name}'s Shop ===");

        foreach (var itemKey in items.OfType<string>())
        {
            var itemTemplate = _itemTemplateCache?.Get(itemKey);
            if (itemTemplate is null)
                continue;

            var price = (long)itemTemplate.BaseValue;
            ctx.Reply($"{itemTemplate.Icon} {itemTemplate.Name}: {price} gold");
        }
    }

    private static void Buy(CommandContext ctx)
    {
        if (!ctx.HasArgument)
        {
            ctx.Reply("Buy what?");
            return;
        }

        if (_mobTemplateCache is null || _itemTemplateCache is null)
        {
            ctx.Reply("The shop is not available.");
            return;
        }

        var character = ctx.Actor.Character;
        var itemName = ctx.Argument;

        // Find shopkeeper in current room
        var shopkeeper = ctx.World.MobsIn(character.RoomKey)
            .FirstOrDefault(m =>
            {
                var template = _mobTemplateCache.Get(m.TemplateKey);
                return template?.Behavior.TryGetValue("shopkeeper", out var isShop) == true &&
                       isShop is bool b && b;
            });

        if (shopkeeper is null)
        {
            ctx.Reply("There is no shopkeeper here.");
            return;
        }

        var shopkeeperTemplate = _mobTemplateCache.Get(shopkeeper.TemplateKey);
        if (shopkeeperTemplate?.Behavior.TryGetValue("sells", out var sellsList) != true ||
            sellsList is not List<object> items)
        {
            ctx.Reply($"{shopkeeperTemplate?.Name ?? "The shopkeeper"} has nothing to sell.");
            return;
        }

        // Find the requested item in the shop's inventory
        var itemKey = items.OfType<string>()
            .FirstOrDefault(k => k.EndsWith(itemName, StringComparison.OrdinalIgnoreCase));

        if (itemKey is null)
        {
            ctx.Reply("The shopkeeper doesn't have that.");
            return;
        }

        var itemTemplate = _itemTemplateCache.Get(itemKey);
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

        // Spawn the item
        var zone = ctx.World.FindZone(character.RoomKey.Zone);
        var world = zone is not null ? ctx.World.FindWorld(zone.WorldKey) : null;

        if (zone is null || world is null)
        {
            ctx.Reply("The shop is temporarily unavailable.");
            return;
        }

        var spawner = new ItemSpawner();
        var spawnedItem = spawner.SpawnAsync(itemTemplate, zone, world, character.RoomKey).Result;

        // Create item instance and add to inventory
        var instance = new DikuWeb.Domain.Items.ItemInstance
        {
            Id = Guid.NewGuid(),
            TemplateKey = itemTemplate.Key,
            TemplateName = itemTemplate.Name,
            Icon = itemTemplate.Icon,
            RoomKey = character.RoomKey.ToString(),
            ResolvedStats = new(itemTemplate.BaseStats),
            SpawnMultipliers = spawnedItem.SpawnMultipliers,
            Value = spawnedItem.Value,
            State = [],
        };

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

        if (_mobTemplateCache is null || _itemTemplateCache is null || _options is null)
        {
            ctx.Reply("The shop is not available.");
            return;
        }

        var character = ctx.Actor.Character;
        var itemName = ctx.Argument;

        // Find shopkeeper in current room
        var shopkeeper = ctx.World.MobsIn(character.RoomKey)
            .FirstOrDefault(m =>
            {
                var template = _mobTemplateCache.Get(m.TemplateKey);
                return template?.Behavior.TryGetValue("shopkeeper", out var isShop) == true &&
                       isShop is bool b && b;
            });

        if (shopkeeper is null)
        {
            ctx.Reply("There is no shopkeeper here.");
            return;
        }

        var shopkeeperTemplate = _mobTemplateCache.Get(shopkeeper.TemplateKey);

        // Find the item in inventory
        var inventory = ctx.World.InventoryOf(character.Id);
        var itemToSell = inventory
            .FirstOrDefault(i => i.TemplateName.EndsWith(itemName, StringComparison.OrdinalIgnoreCase));

        if (itemToSell is null)
        {
            ctx.Reply("You don't have that.");
            return;
        }

        // Check if it's a quest item (stored in ItemInstance state)
        if (itemToSell.State.TryGetValue("questItem", out var questItemFlag) &&
            questItemFlag is bool isQuestItem && isQuestItem)
        {
            ctx.Reply(
                $"{NarrationHelper.WithArticle(shopkeeperTemplate?.Name ?? "shopkeeper", capitalize: true)} refuses to buy a quest item.");
            return;
        }

        // Calculate sellback price
        var sellbackPercent = _options.ShopSellbackPercent ?? 0.5m;
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
            $"{character.Name} sells something to {NarrationHelper.WithArticle(shopkeeperTemplate?.Name ?? "shopkeeper")}.",
            "activity");
    }
}
