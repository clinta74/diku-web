using System.Globalization;
using DikuWeb.Domain.Abilities;
using DikuWeb.Domain.Accounts;
using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Combat;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Items;
using DikuWeb.Domain.Narration;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Abilities;
using DikuWeb.Engine.Inhabitants;
using DikuWeb.Engine.Protocol;
using DikuWeb.Engine.Quests;
using DikuWeb.Engine.Spawning;
using DikuWeb.Engine.Time;

namespace DikuWeb.Engine.Commands;

/// <summary>
/// The command table and its handlers (PLAN.md §3.2 and Phase definitions).
/// </summary>
public sealed class CommandRegistry
{
    private readonly List<CommandDefinition> _commands;

    /// <remarks>
    /// The quest and template caches, the quest save queue, and <c>EngineOptions</c> used to be
    /// taken here so <see cref="ShopCommands"/> and <see cref="QuestCommands"/> could stash them
    /// in statics. They now travel on the <see cref="CommandContext"/> with every command, so the
    /// registry no longer needs any of them - which is also what stopped one registry's caches
    /// from serving another's world.
    /// </remarks>
    public CommandRegistry(
        AbilityCache? abilityCache = null,
        IMobTemplateRepository? mobTemplates = null,
        IItemTemplateRepository? itemTemplates = null,
        MobSpawner? mobSpawner = null,
        ItemSpawner? itemSpawner = null,
        IGameClock? clock = null,
        Domain.Abilities.Effects.EffectRegistry? effects = null)
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

        // Full word required, for the same reason quit demands all four: nothing comes back.
        _commands.Add(new CommandDefinition(
            "destroy", 7, "destroy <item> - permanently destroy an item you're carrying", Destroy));

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
        AbilityCommands.Register(_commands, abilityCache, clock, effects);
        QuestCommands.Register(_commands);

        // After the quest verbs, so a bare "t" still reaches talk rather than tell. Prefix
        // matching is first-match-wins, and the older verb keeps the shorter abbreviation.
        PartyCommands.Register(_commands);
        ChannelCommands.Register(_commands);
        TravelCommands.Register(_commands);
        ShopCommands.Register(_commands);
        StatusCommands.Register(_commands);
        BuilderCommands.Register(_commands, mobTemplates, itemTemplates, mobSpawner, itemSpawner);
        AdminCommands.Register(_commands);
        AdminWorldCommands.Register(_commands);
    }

    public IReadOnlyList<CommandDefinition> Commands => _commands;

    public CommandDefinition? Find(string verb) =>
        string.IsNullOrEmpty(verb)
            ? null
            : _commands.FirstOrDefault(c => c.Matches(verb));

    /// <summary>
    /// Punctuation that stands in for a whole verb, with no space after it.
    /// </summary>
    /// <remarks>
    /// The two commands worth the keystroke are the two typed most often and always with an
    /// argument, so <c>'hello</c> and <c>;grins</c> read naturally. Expanded here rather than
    /// registered as verbs, because a <see cref="CommandDefinition"/> is matched by prefix and
    /// would demand the space - <c>' hello</c> works, <c>'hello</c> would not.
    ///
    /// Deliberately a short list. Every character claimed here is one that can never start an
    /// ordinary command, and a shortcut nobody remembers costs more than it saves.
    /// </remarks>
    private static readonly Dictionary<char, string> _verbShortcuts = new()
    {
        ['\''] = "say",
        ['"'] = "say",
        [';'] = "emote",
        [':'] = "emote",
    };

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

        if (_verbShortcuts.TryGetValue(trimmed[0], out var shortcut))
        {
            return (shortcut, trimmed[1..].TrimStart());
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

        // Snared. This mostly matters out of combat - in a fight the check above has already
        // refused - but a root that let you walk away the moment the fight ended would be a
        // strange kind of snare.
        if (ctx.World.IsRooted(character.Id, ctx.Clock?.CurrentPulse ?? 0L))
        {
            var holding = ctx.World.RootName(character.Id, ctx.Clock?.CurrentPulse ?? 0L) ?? "something";
            ctx.Reply($"You cannot go anywhere — you are {holding}.", "bad");
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

        if (ctx.RefusedForMute())
        {
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

        // A shortcut nobody discovers is not a shortcut. Listed from the same table the parser
        // reads, so adding one here is impossible to forget.
        var shortcuts = string.Join(
            ", ",
            _verbShortcuts
                .GroupBy(pair => pair.Value, StringComparer.Ordinal)
                .Select(group => $"{string.Join(" or ", group.Select(p => p.Key))} = {group.Key}"));

        spans.Add(new TextSpan($"\nNo space needed after {shortcuts}.", "dim"));

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

        // Look in hand first, then on the ground, then at whoever is standing here. "examine" is
        // most often aimed at something you are already holding - but it used to look at nothing
        // else at all, so examining an NPC in front of you reported that it was not there.
        var inventory = ctx.World.InventoryOf(ctx.Actor.CharacterId);
        var targetItem = FindItemByName(inventory, ctx.Argument)
            ?? FindItemByName(ctx.World.ItemsIn(ctx.Actor.RoomKey), ctx.Argument);

        if (targetItem is not null)
        {
            ExamineItem(ctx, targetItem);
            return;
        }

        var targetMob = NameMatch.Best(
            ctx.World.MobsIn(ctx.Actor.RoomKey), ctx.Argument, m => m.TemplateName, m => m.TemplateKey);

        if (targetMob is not null)
        {
            ExamineMob(ctx, targetMob);
            return;
        }

        ctx.Reply($"You don't see any {ctx.Argument} here.", "bad");
    }

    private static void ExamineItem(CommandContext ctx, ItemInstance item)
    {
        var template = ctx.ItemTemplates?.Get(item.TemplateKey);
        var article = NarrationHelper.WithDefiniteArticle(item.TemplateName);

        var spans = new List<TextSpan>
        {
            new($"You examine {article}.", "heading"),
        };

        var description = template?.Description;
        if (!string.IsNullOrWhiteSpace(description))
        {
            spans.Add(new TextSpan($"\n{description}"));
        }

        spans.Add(new TextSpan($"\n{UsageProse(template?.Slot)}", "dim"));

        if (ItemState.IsQuestItem(item))
        {
            spans.Add(new TextSpan("\nIt is bound to a quest — it cannot be sold or destroyed.", "dim"));
        }

        AppendBuilderDetail(ctx, spans, item.TemplateKey, BuilderLinks.ToItem(item.TemplateKey), () =>
        [
            ("value", item.Value.ToString(CultureInfo.InvariantCulture)),
            ("weight", template?.Weight.ToString(CultureInfo.InvariantCulture) ?? "?"),
            ("slot", template?.Slot?.ToString() ?? "none"),
            ("speed", template?.AttackDelayPulses?.ToString(CultureInfo.InvariantCulture) ?? "—"),
            ("verb", template?.AttackVerb ?? "—"),
            ("stats", Describe(item.ResolvedStats)),
        ]);

        ctx.Actor.Send(new OutboundEvent(EventTypes.Text, new TextPayload(spans)));
    }

    private static void ExamineMob(CommandContext ctx, Mob mob)
    {
        var template = ctx.MobTemplates?.Get(mob.TemplateKey);
        var displayName = string.IsNullOrEmpty(mob.TemplateName) ? mob.TemplateKey : mob.TemplateName;
        var article = NarrationHelper.WithDefiniteArticle(displayName);

        var spans = new List<TextSpan>
        {
            new($"You examine {article}.", "heading"),
        };

        if (!string.IsNullOrWhiteSpace(template?.Description))
        {
            spans.Add(new TextSpan($"\n{template.Description}"));
        }

        // Health as a fraction rather than a number: a player should be able to see that
        // something is hurt without being handed the combat log.
        spans.Add(new TextSpan($"\n{ConditionProse(mob)}", "dim"));

        var disposition = MobBehavior.DispositionOf(template?.Behavior);
        if (disposition == MobDisposition.Npc)
        {
            spans.Add(new TextSpan("\nThey are not someone you can fight.", "dim"));
        }
        else if (disposition == MobDisposition.Aggressive)
        {
            spans.Add(new TextSpan("\nIt watches you the way something decides to attack.", "bad"));
        }

        if (MobBehavior.IsShopkeeper(template?.Behavior))
        {
            spans.Add(new TextSpan("\nThey keep a shop — try 'list'.", "good"));
        }

        AppendBuilderDetail(ctx, spans, mob.TemplateKey, BuilderLinks.ToMob(mob.TemplateKey), () =>
        [
            ("level", mob.Level.ToString(CultureInfo.InvariantCulture)),
            ("health", $"{mob.Vitals.Health}/{mob.Vitals.HealthMax}"),
            ("xp", mob.ResolvedXp.ToString(CultureInfo.InvariantCulture)),
            ("gold", mob.ResolvedGold.ToString(CultureInfo.InvariantCulture)),
            ("type", MobBehavior.NameOf(disposition)),
            ("spawner", mob.SpawnerId?.ToString() ?? "—"),
            ("stats", Describe(mob.ResolvedStats)),
        ]);

        ctx.Actor.Send(new OutboundEvent(EventTypes.Text, new TextPayload(spans)));
    }

    /// <summary>How badly hurt something looks, without printing its hit points.</summary>
    private static string ConditionProse(Mob mob)
    {
        if (mob.Vitals.HealthMax <= 0)
        {
            return "It is hard to tell what shape it is in.";
        }

        var fraction = (double)mob.Vitals.Health / mob.Vitals.HealthMax;

        return fraction switch
        {
            >= 1.0 => "It is unhurt.",
            >= 0.75 => "It has been scratched.",
            >= 0.5 => "It is bleeding.",
            >= 0.25 => "It is badly hurt.",
            > 0 => "It is barely standing.",
            _ => "It is not long for this world.",
        };
    }

    /// <summary>
    /// Appends the template key, the raw numbers, and a link into the builder.
    /// </summary>
    /// <remarks>
    /// Builders only. A player has no use for a template key and no way to follow the link, and
    /// showing it would put the builder's existence on the wire for everyone. The values are
    /// gathered lazily so a player's <c>examine</c> does no work to produce a block it will
    /// never see.
    /// </remarks>
    private static void AppendBuilderDetail(
        CommandContext ctx,
        List<TextSpan> spans,
        string templateKey,
        string builderPath,
        Func<IReadOnlyList<(string Label, string Value)>> details)
    {
        if (!ctx.Actor.Role.Satisfies(AccountRole.Builder))
        {
            return;
        }

        // The key *is* the link. It is already on screen and it is already the thing the builder
        // would go looking for, so a separate "Open in builder" line was one row of pure
        // scaffolding under every block.
        //
        // The line break goes in a plain span, never in the link. A link renders as an
        // inline-block <button> and the scrollback's white-space: pre-wrap is inherited into it,
        // so a newline inside one makes the *button* several lines tall instead of breaking the
        // line, and its label drops to the baseline of whatever preceded it.
        spans.Add(new TextSpan("\n\n"));
        spans.Add(new TextSpan($"[{templateKey}]", "link", builderPath));

        var rows = details();

        // Padded to the widest label in *this* block rather than to a constant, so the columns
        // stay aligned when a field is added and no block is indented further than it needs to
        // be. The scrollback is monospace with pre-wrap, so the spaces survive to the screen -
        // without the padding, "xp:" and "spawner:" start their values seven columns apart.
        var width = rows.Count == 0 ? 0 : rows.Max(r => r.Label.Length);

        foreach (var (label, value) in rows)
        {
            spans.Add(new TextSpan($"\n  {(label + ":").PadRight(width + 2)}{value}", "dim"));
        }
    }

    /// <summary>A stat bag as one readable line, or an em dash when it is empty.</summary>
    private static string Describe(IReadOnlyDictionary<string, object>? stats) =>
        stats is null || stats.Count == 0
            ? "—"
            : string.Join(", ", stats.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => $"{kv.Key}={kv.Value}"));

    /// <summary>Prose telling a player, in-world, how (or whether) an item can be equipped.</summary>
    private static string UsageProse(ItemSlot? slot) => slot switch
    {
        ItemSlot.Head => "It looks made to be worn on your head.",
        ItemSlot.Chest => "It looks made to be worn over your chest.",
        ItemSlot.Hands => "It looks made to be worn on your hands.",
        ItemSlot.Legs => "It looks made to guard your legs.",
        ItemSlot.Feet => "It looks made to be worn on your feet.",
        ItemSlot.MainHand => "It has the balance of something meant for your main hand — you could wield it.",
        ItemSlot.OffHand => "It sits ready for your off hand — you could wield it.",
        ItemSlot.Trinket => "It looks like it would make a fine trinket.",
        _ => "It isn't something you can wear or wield.",
    };

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
        ctx.ItemSaveQueue?.Enqueue(targetItem);

        ctx.Reply($"You take {NarrationHelper.WithDefiniteArticle(targetItem.TemplateName)}.", "good");
        ctx.Broadcast($"{ctx.Actor.Name} takes {NarrationHelper.WithDefiniteArticle(targetItem.TemplateName)}.", "movement");
        ctx.MarkRoomForRefresh(ctx.Actor.RoomKey);
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
        ctx.ItemSaveQueue?.Enqueue(targetItem);

        ctx.Reply($"You drop {NarrationHelper.WithDefiniteArticle(targetItem.TemplateName)}.", "good");
        ctx.Broadcast($"{ctx.Actor.Name} drops {NarrationHelper.WithDefiniteArticle(targetItem.TemplateName)}.", "movement");
        ctx.MarkRoomForRefresh(ctx.Actor.RoomKey);
    }

    /// <summary>
    /// Takes an item out of the world for good.
    /// </summary>
    /// <remarks>
    /// Drop leaves something to pick back up; this does not. The two losses a player would most
    /// regret are refused outright rather than merely warned about - what they are wearing, which
    /// they can only have destroyed by mistyping, and a quest item, which would strand the quest
    /// with no way to re-earn it. The shopkeeper refuses quest items on the same grounds.
    /// </remarks>
    private static void Destroy(CommandContext ctx)
    {
        if (!ctx.HasArgument)
        {
            ctx.Reply("Destroy what?", "bad");
            return;
        }

        var inventory = ctx.World.InventoryOf(ctx.Actor.CharacterId);
        var targetItem = FindItemByName(inventory, ctx.Argument);

        if (targetItem is null)
        {
            ctx.Reply($"You don't have {ctx.Argument}.", "bad");
            return;
        }

        var article = NarrationHelper.WithDefiniteArticle(targetItem.TemplateName);

        if (targetItem.EquippedSlot is not null)
        {
            ctx.Reply($"You'll have to remove {article} first.", "bad");
            return;
        }

        if (ItemState.IsQuestItem(targetItem))
        {
            ctx.Reply($"Something stays your hand: {article} is bound to a quest.", "bad");
            return;
        }

        // Out of the world and out of storage. Forgetting it in memory alone would hand it back
        // on the next load, still owned by the player who destroyed it.
        ctx.World.RemoveItem(targetItem);
        ctx.ItemSaveQueue?.EnqueueDelete(targetItem.Id);

        ctx.Reply($"You destroy {article}. It is gone for good.", "good");
        ctx.Broadcast($"{ctx.Actor.Name} destroys {article}.", "movement");
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
            ctx.Reply($"You're already wearing {NarrationHelper.WithDefiniteArticle(targetItem.TemplateName)}.", "bad");
            return;
        }

        var article = NarrationHelper.WithDefiniteArticle(targetItem.TemplateName);
        var slot = SlotFor(ctx, targetItem);

        if (slot is null)
        {
            ctx.Reply($"You can't wear {article} — it isn't something you can equip.", "bad");
            return;
        }

        // "wear" is for armour and trinkets; hands are wielded, not worn. Sending someone to the
        // right verb is friendlier than silently jamming a sword onto their chest.
        if (IsHandSlot(slot.Value))
        {
            ctx.Reply($"You can't wear {article} — try wielding it instead.", "bad");
            return;
        }

        if (!TryEquip(ctx, targetItem, slot.Value, "wear", "wears"))
        {
            return;
        }
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
            ctx.Reply($"You're already wielding {NarrationHelper.WithDefiniteArticle(targetItem.TemplateName)}.", "bad");
            return;
        }

        var article = NarrationHelper.WithDefiniteArticle(targetItem.TemplateName);
        var slot = SlotFor(ctx, targetItem);

        if (slot is null)
        {
            ctx.Reply($"You can't wield {article} — it isn't something you can equip.", "bad");
            return;
        }

        // The mirror of Wear: only hand slots are wielded; armour is worn.
        if (!IsHandSlot(slot.Value))
        {
            ctx.Reply($"You can't wield {article} — try wearing it instead.", "bad");
            return;
        }

        if (!TryEquip(ctx, targetItem, slot.Value, "wield", "wields"))
        {
            return;
        }

        // Striking with a second weapon is trained, not innate. Say so, or an untrained
        // character reads their off hand doing nothing as a bug.
        if (slot.Value == ItemSlot.OffHand && !CanStrikeWithOffHand(ctx.Actor.Character))
        {
            ctx.Reply(
                $"You settle {article} into your off hand, though you've not the training to strike with it.",
                "dim");
        }
    }

    private static bool CanStrikeWithOffHand(Character character) =>
        AbilityProgression.KnowsPassive(character.Path, character.Level, PassiveKeys.DualWield);

    /// <summary>
    /// Reports the off hand only when there is something to report, and is honest when it is
    /// carrying rather than fighting - a weapon that shows a damage range it will never roll is
    /// exactly the kind of lie the stats screen was rewritten to stop telling.
    /// </summary>
    private static void AppendOffHand(
        CommandContext ctx,
        List<TextSpan> spans,
        Character character,
        IReadOnlyList<ItemInstance> equipped)
    {
        var offHand = equipped.FirstOrDefault(i => i.EquippedSlot == ItemSlot.OffHand);
        if (offHand is null)
        {
            return;
        }

        var displayName = string.IsNullOrEmpty(offHand.TemplateName) ? offHand.TemplateKey : offHand.TemplateName;
        var template = ctx.ItemTemplates?.Get(offHand.TemplateKey);

        spans.Add(new TextSpan($"\n\nOff Hand: {displayName}", "heading"));

        if (template?.AttackDelayPulses is null)
        {
            spans.Add(new TextSpan("\n  Not a weapon — it never strikes.", "dim"));
            return;
        }

        if (!CanStrikeWithOffHand(character))
        {
            spans.Add(new TextSpan("\n  Carried only — you've not the training to strike with it.", "dim"));
            return;
        }

        var offAttack = EquipmentResolver.ResolveAttackerStatsForHand(
            character.Level, character.Attributes.MightModifier, equipped, ItemSlot.OffHand);
        var delay = AttackTiming.Clamp(template.AttackDelayPulses);
        var independent = AbilityProgression.KnowsPassive(
            character.Path, character.Level, PassiveKeys.Ambidextrous);

        spans.Add(new TextSpan(
            $"\n  Damage Range: {offAttack.MinDamage + offAttack.BaseDamage}-{offAttack.MaxDamage + offAttack.BaseDamage}"));
        spans.Add(new TextSpan($"\n  Speed: one swing every {delay * 0.25:0.##}s"));
        spans.Add(new TextSpan(
            independent
                ? "\n  Ambidextrous — strikes on its own timing."
                : "\n  Follows your main hand.",
            "dim"));
    }

    /// <summary>
    /// The slot an item declares on its template, or null if it is not equippable. Resolved from
    /// the template cache because an <see cref="ItemInstance"/> only caches its key.
    /// </summary>
    private static ItemSlot? SlotFor(CommandContext ctx, ItemInstance item) =>
        ctx.ItemTemplates?.Get(item.TemplateKey)?.Slot;

    private static bool IsHandSlot(ItemSlot slot) =>
        slot is ItemSlot.MainHand or ItemSlot.OffHand;

    /// <summary>
    /// Equips into the item's own slot, refusing if that slot is already filled. Returns false
    /// (having replied) when the slot is taken, so callers can bail without repeating the check.
    /// </summary>
    private static bool TryEquip(
        CommandContext ctx,
        ItemInstance item,
        ItemSlot slot,
        string selfVerb,
        string othersVerb)
    {
        var occupant = ctx.World.InventoryOf(ctx.Actor.CharacterId)
            .FirstOrDefault(i => i.EquippedSlot == slot);

        if (occupant is not null)
        {
            var occupantName = NarrationHelper.WithDefiniteArticle(occupant.TemplateName);
            ctx.Reply($"You're already using your {SlotName(slot)} for {occupantName}.", "bad");
            return false;
        }

        var article = NarrationHelper.WithDefiniteArticle(item.TemplateName);
        ctx.World.EquipItem(item, slot);
        ctx.ItemSaveQueue?.Enqueue(item);

        ctx.Reply($"You {selfVerb} {article}.", "good");
        ctx.Broadcast($"{ctx.Actor.Name} {othersVerb} {article}.", "movement");
        return true;
    }

    /// <summary>A human-readable name for a slot, for prose like "your main hand".</summary>
    private static string SlotName(ItemSlot slot) => slot switch
    {
        ItemSlot.MainHand => "main hand",
        ItemSlot.OffHand => "off hand",
        _ => slot.ToString().ToLowerInvariant(),
    };

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
            ctx.Reply($"You're not wearing {NarrationHelper.WithDefiniteArticle(targetItem.TemplateName)}.", "bad");
            return;
        }

        ctx.World.UnequipItem(targetItem);
        ctx.ItemSaveQueue?.Enqueue(targetItem);

        ctx.Reply($"You remove {NarrationHelper.WithDefiniteArticle(targetItem.TemplateName)}.", "good");
        ctx.Broadcast($"{ctx.Actor.Name} removes {NarrationHelper.WithDefiniteArticle(targetItem.TemplateName)}.", "movement");
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
        ctx.ItemSaveQueue?.Enqueue(targetItem);

        ctx.Reply($"You give {NarrationHelper.WithDefiniteArticle(targetItem.TemplateName)} to {targetPlayer.Name}.", "good");
        targetPlayer.SendText($"{ctx.Actor.Name} gives you {NarrationHelper.WithDefiniteArticle(targetItem.TemplateName)}.", "good");
        ctx.Broadcast($"{ctx.Actor.Name} gives {NarrationHelper.WithDefiniteArticle(targetItem.TemplateName)} to {targetPlayer.Name}.", "movement");
        ctx.MarkRoomForRefresh(ctx.Actor.RoomKey);
    }

    /// <summary>
    /// Third-person prose, shown to the room and to the person who wrote it.
    /// </summary>
    /// <remarks>
    /// The echo used to be missing, so typing <c>;grins</c> produced nothing at all on your own
    /// screen and there was no way to tell a working emote from a swallowed one — in an empty
    /// room, no way to tell at all.
    ///
    /// The same third-person line rather than a second-person rewrite, because rewriting means
    /// conjugating: "grins" → "grin" is easy, and there is no rule that also turns "waves at the
    /// fire" or "is lost in thought" into something that reads. Showing what everyone else sees is
    /// both honest and the thing a player wants to check.
    /// </remarks>
    private static void Emote(CommandContext ctx)
    {
        if (!ctx.HasArgument)
        {
            ctx.Reply("Emote what?", "bad");
            return;
        }

        // Covered too: an emote is free text in front of other players, so a mute that let it
        // through would be a mute you could talk around.
        if (ctx.RefusedForMute())
        {
            return;
        }

        var line = $"{ctx.Actor.Name} {ctx.Argument}";

        ctx.Reply(line, "emote");
        ctx.Broadcast(line, "emote");
    }

    private static void Stats(CommandContext ctx)
    {
        var character = ctx.Actor.Character;
        var items = ctx.World.InventoryOf(character.Id);
        var equipped = items.Where(i => i.EquippedSlot is not null).ToList();

        // Read through the same resolver combat uses. This screen used to derive its numbers
        // from level with its own formula, so it reported a damage range the game would never
        // roll - the reason a sword could show 4-12 here while landing 3s in a fight.
        var attack = EquipmentResolver.ResolveAttackerStatsForHand(
            character.Level,
            character.Attributes.MightModifier,
            equipped,
            ItemSlot.MainHand);

        var defense = EquipmentResolver.ResolveDefenderStats(
            character.Attributes.AgilityModifier,
            equipped);

        // What the dice actually produce, modifier included, before the target's armour.
        var minDamage = attack.MinDamage + attack.BaseDamage;
        var maxDamage = attack.MaxDamage + attack.BaseDamage;

        var mainHand = equipped.FirstOrDefault(i => i.EquippedSlot == ItemSlot.MainHand);
        var mainDelay = AttackTiming.Clamp(
            mainHand is null ? null : ctx.ItemTemplates?.Get(mainHand.TemplateKey)?.AttackDelayPulses);

        var spans = new List<TextSpan>
        {
            new($"Combat Stats", "heading"),
            new($"\nLevel: {character.Level}"),
            new($"\nGold: {character.Gold:N0}"),
            new($"\nDamage Range: {minDamage}-{maxDamage}", "good"),
            new($"\n  Dice: {attack.MinDamage}-{attack.MaxDamage}, Might bonus: {attack.BaseDamage:+#;-#;+0}"),
            new($"\n  Speed: one swing every {mainDelay * 0.25:0.##}s"),
            new($"\nAttack Rating: {attack.AttackRating}", "good"),
            new($"\nDefense: {10 + defense.DefenseRating}", "good"),
            new($"\n  Armour: {defense.ArmorFlat} flat, {defense.ArmorPercent:P0} reduction"),
        };

        AppendOffHand(ctx, spans, character, equipped);

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

        AppendBuilderSheet(ctx, spans, equipped);

        ctx.Actor.Send(new OutboundEvent(EventTypes.Text, new TextPayload(spans)));
    }

    /// <summary>
    /// The builder's half of the stats screen: where you are, what you are wearing by key, and
    /// a way into the editor for each.
    /// </summary>
    /// <remarks>
    /// A builder testing a change is usually standing in the thing they just edited, so the most
    /// useful link on this screen is the room itself - it saves finding the room again in a tree
    /// that may be several zones deep.
    /// </remarks>
    private static void AppendBuilderSheet(
        CommandContext ctx,
        List<TextSpan> spans,
        IReadOnlyList<ItemInstance> equipped)
    {
        if (!ctx.Actor.Role.Satisfies(AccountRole.Builder))
        {
            return;
        }

        var roomKey = ctx.Actor.RoomKey;
        var zone = ctx.World.FindZone(roomKey.ZoneKey);

        spans.Add(new TextSpan("\n\nBuilder", "heading"));

        // Same column treatment as the examine block. Each key carries its own link, so there is
        // no trailing list of "Open X in builder" lines repeating keys already in the column.
        var rows = new List<(string Label, string Value, string? Link)>
        {
            ("room", roomKey.ToString(), BuilderLinks.ToRoom(roomKey)),
        };

        if (zone is not null)
        {
            // The dial this room's contents are scaled by. Reading it here saves guessing why a
            // rat in this zone hits harder than the same rat next door.
            // "0.##" so a neutral dial reads "×1" rather than "×1.0" - a column of ".0" is noise
            // that makes the two factors a builder actually changed harder to pick out.
            var m = zone.Multipliers;
            rows.Add(("zone", zone.Key, null));
            rows.Add((
                "scaling",
                $"strength ×{m.Strength:0.##}  damage ×{m.Damage:0.##}  " +
                $"xp ×{m.Xp:0.##}  gold ×{m.Gold:0.##}",
                null));
        }

        foreach (var item in equipped.OrderBy(i => i.EquippedSlot))
        {
            rows.Add((
                item.EquippedSlot?.ToString() ?? "held",
                item.TemplateKey,
                BuilderLinks.ToItem(item.TemplateKey)));
        }

        var width = rows.Max(r => r.Label.Length);

        foreach (var (label, value, link) in rows)
        {
            // The label and its padding are a plain span, so neither the line break nor the
            // alignment ever lands inside a link - see AppendBuilderDetail for what that costs.
            spans.Add(new TextSpan($"\n  {(label + ":").PadRight(width + 2)}", "dim"));
            spans.Add(link is null
                ? new TextSpan(value, "dim")
                : new TextSpan(value, "link", link));
        }

    }

    /// <summary>
    /// The item in this list that the player meant.
    /// </summary>
    /// <remarks>
    /// This demanded the whole display name or the whole key, so an "old coin" answered to
    /// "old coin" and "old-coin" and nothing else - <c>get coin</c> failed on the only thing in
    /// the room. See <see cref="NameMatch"/> for what counts as a match now.
    /// </remarks>
    private static ItemInstance? FindItemByName(IEnumerable<ItemInstance> items, string name) =>
        NameMatch.Best(items, name, i => i.TemplateName, i => i.TemplateKey);

    private static bool IsDirection(string name) =>
        DirectionExtensions.All.Any(d => d.ToLowerName() == name);
}
