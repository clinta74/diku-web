using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Items;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Abilities;
using DikuWeb.Engine.Protocol;
using DikuWeb.Engine.Quests;
using DikuWeb.Engine.Spawning;

namespace DikuWeb.Engine.Commands;

/// <summary>
/// The command table and its handlers (PLAN.md §3.2 and Phase definitions).
/// </summary>
public sealed class CommandRegistry
{
    private readonly List<CommandDefinition> _commands;

    public CommandRegistry(
        AbilityCache? abilityCache = null,
        QuestCache? questCache = null,
        IMobTemplateRepository? mobTemplates = null,
        IItemTemplateRepository? itemTemplates = null,
        MobSpawner? mobSpawner = null,
        ItemSpawner? itemSpawner = null)
    {
        _commands = [];

        // Directions first, so a bare "n" or "e" always wins the prefix race against any
        // later verb. This is the single most-typed input in the game.
        foreach (var direction in DirectionExtensions.All)
        {
            var captured = direction;
            _commands.Add(new CommandDefinition(
                direction.ToLowerName(),
                MinLength: 1,
                Help: $"{direction.Abbreviation()} / {direction.ToLowerName()} - move {direction.ToLowerName()}",
                Handler: ctx => Move(ctx, captured)));
        }

        _commands.Add(new CommandDefinition(
            "look", 1, "look (l) - describe the room again", Look));

        _commands.Add(new CommandDefinition(
            "say", 1, "say <message> - speak to everyone in the room", Say));

        _commands.Add(new CommandDefinition(
            "who", 3, "who - list everyone online", Who));

        _commands.Add(new CommandDefinition(
            "inventory", 1, "inventory (i) - list what you're carrying", Inventory));

        _commands.Add(new CommandDefinition(
            "examine", 1, "examine <item> (x) - look closely at an item", Examine));

        _commands.Add(new CommandDefinition(
            "get", 1, "get <item> - pick up an item from the ground", Get));

        _commands.Add(new CommandDefinition(
            "drop", 1, "drop <item> - put down an item from your inventory", Drop));

        _commands.Add(new CommandDefinition(
            "wear", 1, "wear <item> - equip an item on your body", Wear));

        _commands.Add(new CommandDefinition(
            "wield", 1, "wield <item> - equip an item in your hand", Wield));

        _commands.Add(new CommandDefinition(
            "remove", 2, "remove <item> (r) - unequip an item", Remove));

        _commands.Add(new CommandDefinition(
            "give", 1, "give <item> <character> - give an item to someone", Give));

        _commands.Add(new CommandDefinition(
            "emote", 1, "emote <message> - express an emotion or action", Emote));

        _commands.Add(new CommandDefinition(
            "help", 1, "help - this list", Help));

        _commands.Add(new CommandDefinition(
            "stats", 5, "stats - show your damage, armor, and combat stats", Stats));

        // Full word required: quitting by fumbling a key would be a bad surprise.
        _commands.Add(new CommandDefinition(
            "quit", 4, "quit - save and leave the world", Quit));

        CombatCommands.Register(_commands);
        RestCommands.Register(_commands);
        AbilityCommands.Register(_commands, abilityCache);
        QuestCommands.Register(_commands, questCache);
        BuilderCommands.Register(_commands, mobTemplates, itemTemplates, mobSpawner, itemSpawner);
        AdminCommands.Register(_commands);
    }

    public IReadOnlyList<CommandDefinition> Commands => _commands;

    public CommandDefinition? Find(string verb) =>
        string.IsNullOrEmpty(verb)
            ? null
            : _commands.FirstOrDefault(c => c.Matches(verb));

    /// <summary>
    /// Splits raw input into a verb and its remainder. "say  hello  there" keeps the
    /// message intact - only the separator after the verb is consumed.
    /// </summary>
    public static (string Verb, string Argument) Split(string input)
    {
        var trimmed = (input ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return (string.Empty, string.Empty);
        }

        var space = trimmed.IndexOf(' ', StringComparison.Ordinal);
        return space < 0
            ? (trimmed.ToLowerInvariant(), string.Empty)
            : (trimmed[..space].ToLowerInvariant(), trimmed[(space + 1)..].TrimStart());
    }

    // -----------------------------------------------------------------------
    // Handlers
    // -----------------------------------------------------------------------

    private static void Move(CommandContext ctx, Direction direction)
    {
        var character = ctx.Actor.Character;

        // Can't move while in active combat
        if (character.CombatState == CombatState.Fighting)
        {
            ctx.Reply("You can't leave while in combat! Try 'flee' to escape.", "bad");
            return;
        }

        // Can't move while resting or sleeping
        if (character.RestState != CharacterRestState.Stand)
        {
            ctx.Reply("You must stand up first.", "bad");
            return;
        }

        var room = ctx.World.FindRoom(ctx.Actor.RoomKey);
        var exit = room?.ExitTo(direction);

        if (exit is null)
        {
            ctx.Reply($"You cannot go {direction.ToLowerName()} from here.", "bad");
            return;
        }

        if (!ctx.World.TryGetRoom(exit.ToRoomKey, out var destination))
        {
            // A dangling exit: the builder linked it before creating the target, or deleted
            // the target later. Fails closed rather than throwing on the loop (PLAN.md §7.4).
            //
            // A builder gets the offer to materialize it instead - that is what turns walking
            // into the fastest way to lay out geography (§7.6). A player must never learn that
            // the world can be incomplete here.
            if (ctx.Actor.IsBuilder)
            {
                ctx.Reply(
                    $"There is no room {direction.ToLowerName()} yet ('{exit.ToRoomKey}'). "
                    + $"Type 'dig {direction.ToLowerName()}' to create it.",
                    "bad");
            }
            else
            {
                ctx.Reply("The way is blocked.", "bad");
            }

            return;
        }

        var origin = ctx.Actor.RoomKey;

        ctx.Broadcast($"{ctx.Actor.Name} leaves {direction.ToLowerName()}.", "movement");
        ctx.World.Move(ctx.Actor, destination.Key);
        ctx.Broadcast($"{ctx.Actor.Name} arrives from the {direction.Opposite().ToLowerName()}.", "movement");

        ctx.Reply($"You walk {direction.ToLowerName()}.", "movement");
        ctx.View.SendRoom(ctx.World, ctx.Actor, verbose: false);

        // Both rooms changed occupancy, so both maps need redrawing for everyone in them.
        ctx.View.RefreshRoom(ctx.World, origin);
        ctx.View.RefreshRoom(ctx.World, destination.Key);
    }

    private static void Look(CommandContext ctx) =>
        ctx.View.SendRoom(ctx.World, ctx.Actor, verbose: true);

    private static void Say(CommandContext ctx)
    {
        if (!ctx.HasArgument)
        {
            ctx.Reply("Say what?", "bad");
            return;
        }

        ctx.Reply($"You say, '{ctx.Argument}'", "speech");
        ctx.Broadcast($"{ctx.Actor.Name} says, '{ctx.Argument}'", "speech");
    }

    private static void Who(CommandContext ctx)
    {
        var players = ctx.World.AllPlayers
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var spans = new List<TextSpan>
        {
            new($"{players.Count} {(players.Count == 1 ? "player" : "players")} online", "heading"),
        };

        foreach (var player in players)
        {
            var status = player.IsLinkDead ? " [link-dead]" : string.Empty;
            spans.Add(new TextSpan(
                $"\n  {player.Name}  level {player.Character.Level} {player.Character.Path}{status}"));
        }

        ctx.Actor.Send(new OutboundEvent(EventTypes.Text, new TextPayload(spans)));
    }

    private void Help(CommandContext ctx)
    {
        var spans = new List<TextSpan> { new("Commands", "heading") };

        // Directions collapse to one line; listing all six adds nothing.
        spans.Add(new TextSpan("\n  n / e / s / w / u / d - move between rooms"));

        foreach (var command in _commands.Where(
            c => !IsDirection(c.Name) && c.VisibleTo(ctx.Actor.Role)))
        {
            spans.Add(new TextSpan($"\n  {command.Help}"));
        }

        spans.Add(new TextSpan("\n\nMost verbs accept a prefix, so 'l' is 'look'.", "dim"));

        ctx.Actor.Send(new OutboundEvent(EventTypes.Text, new TextPayload(spans)));
    }

    private static void Quit(CommandContext ctx)
    {
        ctx.Reply("You gather your things and step out of the world.", "heading");
        ctx.Broadcast($"{ctx.Actor.Name} leaves the world.", "movement");
        ctx.LeaveRequested = LeaveReason.Quit;
    }

    private static void Inventory(CommandContext ctx)
    {
        var items = ctx.World.InventoryOf(ctx.Actor.CharacterId);
        var equipped = items.Where(i => i.EquippedSlot is not null).ToList();
        var unequipped = items.Where(i => i.EquippedSlot is null).ToList();

        var spans = new List<TextSpan>();

        if (equipped.Count > 0)
        {
            spans.Add(new TextSpan("You are wearing/wielding:", "heading"));
            foreach (var item in equipped.OrderBy(i => i.EquippedSlot))
            {
                var slot = item.EquippedSlot?.ToString() ?? "Unknown";
                var displayName = string.IsNullOrEmpty(item.TemplateName) ? item.TemplateKey : item.TemplateName;
                spans.Add(new TextSpan($"\n  [{slot}] {displayName}"));
            }
        }

        if (unequipped.Count > 0)
        {
            if (equipped.Count > 0)
            {
                spans.Add(new TextSpan("\n\nYou are carrying:", "heading"));
            }
            else
            {
                spans.Add(new TextSpan("You are carrying:", "heading"));
            }

            foreach (var item in unequipped.OrderBy(i => i.TemplateKey))
            {
                var displayName = string.IsNullOrEmpty(item.TemplateName) ? item.TemplateKey : item.TemplateName;
                spans.Add(new TextSpan($"\n  {displayName}"));
            }
        }

        if (items.Count == 0)
        {
            ctx.Reply("You aren't carrying anything.", "dim");
            return;
        }

        ctx.Actor.Send(new OutboundEvent(EventTypes.Text, new TextPayload(spans)));
    }

    private static void Examine(CommandContext ctx)
    {
        if (!ctx.HasArgument)
        {
            ctx.Reply("Examine what?", "bad");
            return;
        }

        ctx.Reply($"You examine the {ctx.Argument}.", "dim");
    }

    private static void Get(CommandContext ctx)
    {
        if (!ctx.HasArgument)
        {
            ctx.Reply("Get what?", "bad");
            return;
        }

        var room = ctx.World.FindRoom(ctx.Actor.RoomKey);
        if (room is null)
        {
            ctx.Reply("You are nowhere.", "bad");
            return;
        }

        var items = ctx.World.ItemsIn(ctx.Actor.RoomKey);
        var targetItem = FindItemByName(items, ctx.Argument);

        if (targetItem is null)
        {
            ctx.Reply($"There is no {ctx.Argument} here.", "bad");
            return;
        }

        ctx.World.PickUpItem(targetItem, ctx.Actor.CharacterId);

        ctx.Reply($"You take the {targetItem.TemplateName}.", "good");
        ctx.Broadcast($"{ctx.Actor.Name} takes the {targetItem.TemplateName}.", "movement");
    }

    private static void Drop(CommandContext ctx)
    {
        if (!ctx.HasArgument)
        {
            ctx.Reply("Drop what?", "bad");
            return;
        }

        var inventory = ctx.World.InventoryOf(ctx.Actor.CharacterId);
        var targetItem = FindItemByName(inventory, ctx.Argument);

        if (targetItem is null)
        {
            ctx.Reply($"You don't have {ctx.Argument}.", "bad");
            return;
        }

        ctx.World.DropItem(targetItem, ctx.Actor.RoomKey);

        ctx.Reply($"You drop the {targetItem.TemplateName}.", "good");
        ctx.Broadcast($"{ctx.Actor.Name} drops the {targetItem.TemplateName}.", "movement");
    }

    private static void Wear(CommandContext ctx)
    {
        if (!ctx.HasArgument)
        {
            ctx.Reply("Wear what?", "bad");
            return;
        }

        var inventory = ctx.World.InventoryOf(ctx.Actor.CharacterId);
        var targetItem = FindItemByName(inventory, ctx.Argument);

        if (targetItem is null)
        {
            ctx.Reply($"You don't have {ctx.Argument}.", "bad");
            return;
        }

        if (targetItem.EquippedSlot is not null)
        {
            ctx.Reply($"You're already wearing the {targetItem.TemplateName}.", "bad");
            return;
        }

        // Determine slot from item configuration
        // For now, just use a default body slot if no slot specified in item
        var slot = ItemSlot.Chest; // Placeholder logic
        ctx.World.EquipItem(targetItem, slot);

        ctx.Reply($"You wear the {targetItem.TemplateName}.", "good");
        ctx.Broadcast($"{ctx.Actor.Name} wears the {targetItem.TemplateName}.", "movement");
    }

    private static void Wield(CommandContext ctx)
    {
        if (!ctx.HasArgument)
        {
            ctx.Reply("Wield what?", "bad");
            return;
        }

        var inventory = ctx.World.InventoryOf(ctx.Actor.CharacterId);
        var targetItem = FindItemByName(inventory, ctx.Argument);

        if (targetItem is null)
        {
            ctx.Reply($"You don't have {ctx.Argument}.", "bad");
            return;
        }

        if (targetItem.EquippedSlot is not null)
        {
            ctx.Reply($"You're already wielding the {targetItem.TemplateName}.", "bad");
            return;
        }

        ctx.World.EquipItem(targetItem, ItemSlot.MainHand);

        ctx.Reply($"You wield the {targetItem.TemplateName}.", "good");
        ctx.Broadcast($"{ctx.Actor.Name} wields the {targetItem.TemplateName}.", "movement");
    }

    private static void Remove(CommandContext ctx)
    {
        if (!ctx.HasArgument)
        {
            ctx.Reply("Remove what?", "bad");
            return;
        }

        var inventory = ctx.World.InventoryOf(ctx.Actor.CharacterId);
        var targetItem = FindItemByName(inventory, ctx.Argument);

        if (targetItem is null)
        {
            ctx.Reply($"You don't have {ctx.Argument}.", "bad");
            return;
        }

        if (targetItem.EquippedSlot is null)
        {
            ctx.Reply($"You're not wearing the {targetItem.TemplateName}.", "bad");
            return;
        }

        ctx.World.UnequipItem(targetItem);

        ctx.Reply($"You remove the {targetItem.TemplateName}.", "good");
        ctx.Broadcast($"{ctx.Actor.Name} removes the {targetItem.TemplateName}.", "movement");
    }

    private static void Give(CommandContext ctx)
    {
        if (!ctx.HasArgument)
        {
            ctx.Reply("Give what to whom?", "bad");
            return;
        }

        var parts = ctx.Argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            ctx.Reply("Give what to whom?", "bad");
            return;
        }

        var itemName = parts[0];
        var targetName = parts[1];

        var inventory = ctx.World.InventoryOf(ctx.Actor.CharacterId);
        var targetItem = FindItemByName(inventory, itemName);

        if (targetItem is null)
        {
            ctx.Reply($"You don't have {itemName}.", "bad");
            return;
        }

        // Check for NPC quest turn-in first (PLAN.md §5.2b)
        if (QuestCommands.TryTurnInQuest(ctx, targetItem.TemplateKey, targetName))
        {
            return;
        }

        var targetPlayer = ctx.World.FindPlayerByName(targetName);
        if (targetPlayer is null)
        {
            ctx.Reply($"There is no one named {targetName} here.", "bad");
            return;
        }

        if (targetPlayer.CharacterId == ctx.Actor.CharacterId)
        {
            ctx.Reply("You can't give items to yourself.", "bad");
            return;
        }

        ctx.World.PickUpItem(targetItem, targetPlayer.CharacterId);

        ctx.Reply($"You give the {targetItem.TemplateName} to {targetPlayer.Name}.", "good");
        targetPlayer.SendText($"{ctx.Actor.Name} gives you the {targetItem.TemplateName}.", "good");
        ctx.Broadcast($"{ctx.Actor.Name} gives the {targetItem.TemplateName} to {targetPlayer.Name}.", "movement");
    }

    private static void Emote(CommandContext ctx)
    {
        if (!ctx.HasArgument)
        {
            ctx.Reply("Emote what?", "bad");
            return;
        }

        ctx.Broadcast($"{ctx.Actor.Name} {ctx.Argument}", "emote");
    }

    private static void Stats(CommandContext ctx)
    {
        var character = ctx.Actor.Character;
        var items = ctx.World.InventoryOf(character.Id);
        var equipped = items.Where(i => i.EquippedSlot is not null).ToList();

        // Calculate damage multiplier from equipped items
        var damageMultiplier = 1.0m;
        var armorMultiplier = 1.0m;

        foreach (var item in equipped.Where(i => i.EquippedSlot == ItemSlot.MainHand || i.EquippedSlot == ItemSlot.OffHand))
        {
            if (item.ResolvedStats.TryGetValue("damageMultiplier", out var dm) && decimal.TryParse(dm.ToString() ?? "1", out var dmVal))
            {
                damageMultiplier *= dmVal;
            }
        }

        foreach (var item in equipped.Where(i => i.EquippedSlot != ItemSlot.MainHand && i.EquippedSlot != ItemSlot.OffHand))
        {
            if (item.ResolvedStats.TryGetValue("armorMultiplier", out var am) && decimal.TryParse(am.ToString() ?? "1", out var amVal))
            {
                armorMultiplier *= amVal;
            }
        }

        // Calculate base damage range (simplified: min 1-3, max 5-15 based on level)
        var minBase = Math.Max(1, character.Level / 2);
        var maxBase = Math.Max(3, character.Level * 2);
        var minDamage = (int)Math.Ceiling(minBase * damageMultiplier);
        var maxDamage = (int)Math.Ceiling(maxBase * damageMultiplier);

        // Calculate armor (simplified: base 0 + equipment bonuses)
        var armor = (int)Math.Ceiling(10 * armorMultiplier);

        var spans = new List<TextSpan>
        {
            new($"Combat Stats", "heading"),
            new($"\nLevel: {character.Level}"),
            new($"\nDamage Range: {minDamage}-{maxDamage}", "good"),
            new($"\n  Base: {minBase}-{maxBase}, Multiplier: {damageMultiplier:F2}x"),
            new($"\nArmor: {armor}", "good"),
            new($"\n  Base: 10, Multiplier: {armorMultiplier:F2}x"),
        };

        if (equipped.Count > 0)
        {
            spans.Add(new TextSpan($"\n\nEquipped Bonuses:", "heading"));
            foreach (var item in equipped.OrderBy(i => i.EquippedSlot))
            {
                var slot = item.EquippedSlot?.ToString() ?? "Unknown";
                var displayName = string.IsNullOrEmpty(item.TemplateName) ? item.TemplateKey : item.TemplateName;
                var statsStr = string.Join(", ", item.ResolvedStats
                    .Where(kv => kv.Key.Contains("Multiplier", StringComparison.OrdinalIgnoreCase))
                    .Select(kv => $"{kv.Key}: {kv.Value}"));

                spans.Add(new TextSpan($"\n  [{slot}] {displayName}"));
                if (!string.IsNullOrEmpty(statsStr))
                {
                    spans.Add(new TextSpan($"\n    {statsStr}", "dim"));
                }
            }
        }

        ctx.Actor.Send(new OutboundEvent(EventTypes.Text, new TextPayload(spans)));
    }

    private static ItemInstance? FindItemByName(IEnumerable<ItemInstance> items, string name)
    {
        var comparison = StringComparison.OrdinalIgnoreCase;

        // Try matching by TemplateName first (display name)
        var byName = items.FirstOrDefault(i =>
            !string.IsNullOrEmpty(i.TemplateName) &&
            i.TemplateName.Equals(name, comparison));

        if (byName is not null)
            return byName;

        // Fall back to TemplateKey match (for backwards compatibility with key-based input)
        return items.FirstOrDefault(i => i.TemplateKey.Equals(name, comparison));
    }

    private static bool IsDirection(string name) =>
        DirectionExtensions.All.Any(d => d.ToLowerName() == name);
}
