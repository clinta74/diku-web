using DikuWeb.Domain.Accounts;
using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Items;
using DikuWeb.Domain.Narration;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine.Mutations;
using DikuWeb.Engine.Protocol;
using DikuWeb.Engine.Spawning;

namespace DikuWeb.Engine.Commands;

/// <summary>
/// Builder verbs typed in the game window (PLAN.md §7.6).
/// </summary>
/// <remarks>
/// These exist for the things that are one short argument. Nobody should type a four-sentence
/// room description onto a command line, so the builder panel owns prose and this owns
/// geography: walking somewhere and saying "there is a room that way" is far faster than
/// clicking through a tree, and it makes the exit graph correct by construction rather than
/// wired up afterwards in a form.
/// </remarks>
internal static class BuilderCommands
{
    private static IMobTemplateRepository? _mobTemplates;
    private static IItemTemplateRepository? _itemTemplates;
    private static MobSpawner? _mobSpawner;
    private static ItemSpawner? _itemSpawner;

    public static void Register(List<CommandDefinition> commands,
        IMobTemplateRepository? mobTemplates = null,
        IItemTemplateRepository? itemTemplates = null,
        MobSpawner? mobSpawner = null,
        ItemSpawner? itemSpawner = null)
    {
        _mobTemplates = mobTemplates;
        _itemTemplates = itemTemplates;
        _mobSpawner = mobSpawner;
        _itemSpawner = itemSpawner;
        // Full words, no prefixes. These are rare, destructive, and share their first letters
        // with movement - "d" must stay "down" forever.
        commands.Add(new CommandDefinition(
            "dig", 3, "dig <dir> [into <zone>] - create and link a room (builder)", Dig, Requires: AccountRole.Builder));

        commands.Add(new CommandDefinition(
            "link", 4, "link <dir> <room-key> - point an exit at an existing room (builder)", Link, Requires: AccountRole.Builder));

        commands.Add(new CommandDefinition(
            "unlink", 6, "unlink <dir> - remove an exit (builder)", Unlink, Requires: AccountRole.Builder));

        commands.Add(new CommandDefinition(
            "rtitle", 6, "rtitle <text> - retitle the room you are in (builder)", RTitle, Requires: AccountRole.Builder));

        commands.Add(new CommandDefinition(
            "rflag", 5, "rflag [<flag> [on|off]] - list or set room flags (builder)", RFlag, Requires: AccountRole.Builder));

        commands.Add(new CommandDefinition(
            "goto", 4, "goto <room-key> - jump anywhere, no exits required (builder)", Goto, Requires: AccountRole.Builder));

        commands.Add(new CommandDefinition(
            "spawn", 5, "spawn <item|mob> <template-key> - create an item or mob here (builder)", Spawn, Requires: AccountRole.Builder));
    }

    private static void Dig(CommandContext ctx)
    {
        if (!RequireBuilder(ctx))
        {
            return;
        }

        // "dig north into aldenmoor.crypt" - the zone suffix is how deliberate border work
        // happens. Without it a dig never silently crosses a zone boundary.
        var argument = ctx.Argument;
        string? zoneKey = null;

        var into = argument.IndexOf(" into ", StringComparison.OrdinalIgnoreCase);
        if (into >= 0)
        {
            zoneKey = argument[(into + 6)..].Trim();
            argument = argument[..into].Trim();
        }

        if (!DirectionExtensions.TryParse(argument, out var direction))
        {
            ctx.Reply("Dig which way? north, east, south, west, up, or down.", "bad");
            return;
        }

        var result = ctx.Edit(new DigRoom(ctx.Actor.RoomKey, direction, Reciprocal: true, zoneKey));

        if (!result.Success)
        {
            ctx.Reply(result.Message ?? "You cannot dig there.", "bad");
            return;
        }

        ctx.Reply(
            $"You open a way {direction.ToLowerName()}. New room: {result.AffectedRoom}.",
            "heading");
        ctx.Broadcast($"{ctx.Actor.Name} opens a way {direction.ToLowerName()}.", "movement");
    }

    private static void Link(CommandContext ctx)
    {
        if (!RequireBuilder(ctx))
        {
            return;
        }

        var parts = ctx.Argument.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2 || !DirectionExtensions.TryParse(parts[0], out var direction))
        {
            ctx.Reply("Usage: link <direction> <world.zone.room>", "bad");
            return;
        }

        if (!RoomKey.TryParse(parts[1].Trim(), out var target))
        {
            ctx.Reply($"'{parts[1].Trim()}' is not a room key.", "bad");
            return;
        }

        var result = ctx.Edit(new LinkExit(ctx.Actor.RoomKey, direction, target));

        ctx.Reply(
            result.Success
                ? $"{direction.ToLowerName()} now leads to {target}."
                : result.Message ?? "That link could not be made.",
            result.Success ? "heading" : "bad");
    }

    private static void Unlink(CommandContext ctx)
    {
        if (!RequireBuilder(ctx))
        {
            return;
        }

        if (!DirectionExtensions.TryParse(ctx.Argument, out var direction))
        {
            ctx.Reply("Usage: unlink <direction>", "bad");
            return;
        }

        var result = ctx.Edit(new UnlinkExit(ctx.Actor.RoomKey, direction));

        ctx.Reply(
            result.Success
                ? $"The way {direction.ToLowerName()} closes."
                : result.Message ?? "That exit could not be removed.",
            result.Success ? "heading" : "bad");
    }

    private static void RTitle(CommandContext ctx)
    {
        if (!RequireBuilder(ctx))
        {
            return;
        }

        if (!ctx.HasArgument)
        {
            ctx.Reply("Usage: rtitle <new title>", "bad");
            return;
        }

        var room = ctx.World.FindRoom(ctx.Actor.RoomKey);
        if (room is null)
        {
            ctx.Reply("You are nowhere at all.", "bad");
            return;
        }

        var upsert = WorldMutationApplier.ToUpsert(room) with { Title = ctx.Argument.Trim() };
        var result = ctx.Edit(upsert);

        ctx.Reply(
            result.Success ? "Retitled." : result.Message ?? "That title could not be set.",
            result.Success ? "heading" : "bad");
    }

    private static void RFlag(CommandContext ctx)
    {
        if (!RequireBuilder(ctx))
        {
            return;
        }

        var room = ctx.World.FindRoom(ctx.Actor.RoomKey);
        if (room is null)
        {
            ctx.Reply("You are nowhere at all.", "bad");
            return;
        }

        // Bare "rflag" lists everything with where each value came from. That answers the
        // question a builder actually has - "why is this room PvP?" - which a plain on/off
        // list would not (PLAN.md §4.10).
        if (!ctx.HasArgument)
        {
            ListFlags(ctx, room);
            return;
        }

        var parts = ctx.Argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var name = parts[0];

        var flag = RoomFlags.All.FirstOrDefault(
            f => string.Equals(f.Key, name, StringComparison.OrdinalIgnoreCase));

        if (flag is null)
        {
            ctx.Reply(
                $"'{name}' is not a room flag. Known: {string.Join(", ", RoomFlags.All.Select(f => f.Key))}",
                "bad");
            return;
        }

        // Three states, not two: "clear" removes the key so the room inherits again, which is
        // different from setting it explicitly false.
        bool? value = parts.Length < 2
            ? true
            : parts[1].ToLowerInvariant() switch
            {
                "on" or "true" or "yes" => true,
                "off" or "false" or "no" => false,
                "clear" or "inherit" => null,
                _ => true,
            };

        var result = ctx.Edit(new SetRoomFlag(room.Key, flag.Key, value));

        if (!result.Success)
        {
            ctx.Reply(result.Message ?? "That flag could not be set.", "bad");
            return;
        }

        ctx.Reply(
            value switch
            {
                true => $"{flag.Key} is now on here.",
                false => $"{flag.Key} is now off here.",
                _ => $"{flag.Key} now inherits from the zone.",
            },
            "heading");
    }

    private static void ListFlags(CommandContext ctx, Room room)
    {
        var spans = new List<TextSpan> { new($"Flags for {room.Key}", "heading") };

        foreach (var flag in RoomFlags.All)
        {
            var resolved = ctx.World.ResolveFlag(room, flag);
            var origin = resolved.Source == FlagSource.Room
                ? string.Empty
                : $"  (from {resolved.Source.ToString().ToLowerInvariant()})";

            spans.Add(new TextSpan(
                $"\n  {flag.Key,-12} {(resolved.Value ? "on" : "off"),-4}{origin}",
                resolved.IsInherited ? "dim" : null));
        }

        ctx.Actor.Send(new OutboundEvent(EventTypes.Text, new TextPayload(spans)));
    }

    private static void Goto(CommandContext ctx)
    {
        if (!RequireBuilder(ctx))
        {
            return;
        }

        if (!RoomKey.TryParse(ctx.Argument.Trim(), out var target))
        {
            ctx.Reply("Usage: goto <world.zone.room>", "bad");
            return;
        }

        if (!ctx.World.TryGetRoom(target, out var destination))
        {
            ctx.Reply($"There is no room '{target}'.", "bad");
            return;
        }

        var origin = ctx.Actor.RoomKey;

        if (origin == target)
        {
            ctx.Reply("You are already there.", "bad");
            return;
        }

        ctx.Broadcast($"{ctx.Actor.Name} steps out of the world.", "movement");
        ctx.World.Move(ctx.Actor, destination.Key);
        ctx.Broadcast($"{ctx.Actor.Name} steps into the world.", "movement");

        ctx.Reply($"You step through to {target}.", "heading");
        ctx.View.SendRoom(ctx.World, ctx.Actor, verbose: true);
        ctx.View.RefreshRoom(ctx.World, origin);
        ctx.View.RefreshRoom(ctx.World, destination.Key);
    }

    /// <summary>
    /// Unknown-verb wording rather than "you are not a builder": a player has no business
    /// learning that these commands exist.
    /// </summary>
    private static void Spawn(CommandContext ctx)
    {
        if (!RequireBuilder(ctx))
        {
            return;
        }

        var parts = ctx.Argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            ctx.Reply("Usage: spawn <item|mob> <template-key>", "bad");
            return;
        }

        var type = parts[0].ToLowerInvariant();
        var templateKey = parts[1];

        if (type == "item")
        {
            SpawnItem(ctx, templateKey);
        }
        else if (type == "mob")
        {
            SpawnMob(ctx, templateKey);
        }
        else
        {
            ctx.Reply("Spawn what? Use 'item' or 'mob'.", "bad");
        }
    }

    private static void SpawnItem(CommandContext ctx, string templateKey)
    {
        if (_itemTemplates == null || _itemSpawner == null)
        {
            ctx.Reply("Item spawning is not available.", "bad");
            return;
        }

        var roomKey = ctx.Actor.RoomKey;
        var room = ctx.World.FindRoom(roomKey);
        if (room == null)
        {
            ctx.Reply("You are nowhere at all.", "bad");
            return;
        }

        var zone = ctx.World.FindZone(room.ZoneKey);
        var world = zone != null ? ctx.World.FindWorld(zone.WorldKey) : null;

        if (zone == null || world == null)
        {
            ctx.Reply("Cannot determine zone/world context.", "bad");
            return;
        }

        // Load template (this is sync, so we need to call async version differently)
        // For now, we'll do this fire-and-forget since builder commands are typed manually
        _ = SpawnItemAsync(ctx, templateKey, zone, world, roomKey);
    }

    private static async System.Threading.Tasks.Task SpawnItemAsync(CommandContext ctx, string templateKey,
        Zone zone, global::DikuWeb.Domain.Worlds.World world, RoomKey roomKey)
    {
        try
        {
            var template = await _itemTemplates!.GetByKeyAsync(templateKey, System.Threading.CancellationToken.None);
            if (template == null)
            {
                ctx.Reply($"Item template '{templateKey}' not found.", "bad");
                return;
            }

            var item = _itemSpawner!.Spawn(template, zone, world, roomKey);
            ctx.World.AddItem(item);
            ctx.ItemSaveQueue?.Enqueue(item);

            ctx.Reply($"Spawned: {template.Icon} {template.Name}");
            ctx.Broadcast($"{ctx.Actor.Name} conjures {NarrationHelper.WithArticle(template.Name)}!", "arrival");
            ctx.View.RefreshRoom(ctx.World, roomKey);
        }
        catch (Exception ex)
        {
            ctx.Reply($"Error spawning item: {ex.Message}", "bad");
        }
    }

    private static void SpawnMob(CommandContext ctx, string templateKey)
    {
        if (_mobTemplates == null || _mobSpawner == null)
        {
            ctx.Reply("Mob spawning is not available.", "bad");
            return;
        }

        var roomKey = ctx.Actor.RoomKey;
        var room = ctx.World.FindRoom(roomKey);
        if (room == null)
        {
            ctx.Reply("You are nowhere at all.", "bad");
            return;
        }

        var zone = ctx.World.FindZone(room.ZoneKey);
        var world = zone != null ? ctx.World.FindWorld(zone.WorldKey) : null;

        if (zone == null || world == null)
        {
            ctx.Reply("Cannot determine zone/world context.", "bad");
            return;
        }

        // Load template and spawn (fire-and-forget like above)
        _ = SpawnMobAsync(ctx, templateKey, zone, world, roomKey);
    }

    private static async System.Threading.Tasks.Task SpawnMobAsync(CommandContext ctx, string templateKey,
        Zone zone, global::DikuWeb.Domain.Worlds.World world, RoomKey roomKey)
    {
        try
        {
            var template = await _mobTemplates!.GetByKeyAsync(templateKey, System.Threading.CancellationToken.None);
            if (template == null)
            {
                ctx.Reply($"Mob template '{templateKey}' not found.", "bad");
                return;
            }

            var mob = _mobSpawner!.Spawn(template, zone, world, roomKey);
            ctx.World.AddMob(mob);

            var displayName = string.IsNullOrEmpty(template.Name) ? template.Key : template.Name;
            ctx.Reply($"Spawned: {template.Icon} {displayName} (level {template.Level})");
            ctx.Broadcast($"{ctx.Actor.Name} conjures {NarrationHelper.WithArticle(displayName)}!", "arrival");
            ctx.View.RefreshRoom(ctx.World, roomKey);
        }
        catch (Exception ex)
        {
            ctx.Reply($"Error spawning mob: {ex.Message}", "bad");
        }
    }

    private static bool RequireBuilder(CommandContext ctx)
    {
        if (ctx.Actor.IsBuilder)
        {
            return true;
        }

        ctx.Reply($"'{ctx.Verb}' is not something you can do. Try 'help'.", "bad");
        return false;
    }
}
