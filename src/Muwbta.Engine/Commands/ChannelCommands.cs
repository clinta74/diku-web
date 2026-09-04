using Muwbta.Engine.World;

namespace Muwbta.Engine.Commands;

/// <summary>
/// Talking to someone who is not in the room (PLAN.md §5.3).
/// </summary>
/// <remarks>
/// Everything here crosses rooms, which is what makes it a different thing from <c>say</c>.
/// The party channel is not here - it lives with <see cref="PartyCommands"/>, because who hears
/// it is a party question rather than a channel one.
/// </remarks>
public static class ChannelCommands
{
    public static void Register(List<CommandDefinition> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        // "te": talk is registered first and answers to a bare "t".
        commands.Add(new CommandDefinition(
            "tell", 2, "tell <who> <message> (te) - speak privately, anywhere in the world", Tell));

        // "rep": remove takes "re", rest takes "r".
        commands.Add(new CommandDefinition(
            "reply", 3, "reply <message> - answer the last person who told you something", Reply));

        // "ch": cast and consider both answer to a bare "c".
        commands.Add(new CommandDefinition(
            "chat", 2, "chat <message> | chat on|off (ch) - the world channel", Chat));
    }

    private static void Tell(CommandContext ctx)
    {
        // Not lowercased: this is who the tell is for, and NameMatch is case-insensitive
        // anyway, so folding the case here would only make the refusal message wrong.
        var (name, message) = CommandText.SplitFirstWord(ctx.Argument, lowercase: false);

        if (string.IsNullOrEmpty(name))
        {
            ctx.Reply("Tell whom?", "bad");
            return;
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            ctx.Reply($"Tell {name} what?", "bad");
            return;
        }

        // Across the whole world, not the room: a tell that only reached the room would be `say`
        // with extra steps. Matched by prefix like every other name, so "te ka hello" works.
        var target = NameMatch.Best(
            ctx.World.AllPlayers.Where(p => p.CharacterId != ctx.Actor.CharacterId),
            name,
            p => p.Name,
            _ => null);

        if (target is null)
        {
            ctx.Reply($"Nobody named '{name}' is online.", "bad");
            return;
        }

        Deliver(ctx, target, message);
    }

    private static void Reply(CommandContext ctx)
    {
        if (!ctx.HasArgument)
        {
            ctx.Reply("Reply what?", "bad");
            return;
        }

        if (ctx.Actor.LastTellFrom is not { } lastId)
        {
            ctx.Reply("Nobody has told you anything.", "bad");
            return;
        }

        if (ctx.World.FindByCharacter(lastId) is not { } target)
        {
            ctx.Reply("They are no longer online.", "bad");
            return;
        }

        Deliver(ctx, target, ctx.Argument);
    }

    /// <remarks>
    /// The mute is checked here rather than in <c>Tell</c> and <c>Reply</c> separately, so the two
    /// entry points cannot disagree about whether a silenced player may still answer.
    /// </remarks>
    private static void Deliver(CommandContext ctx, PlayerActor target, string message)
    {
        if (ctx.RefusedForMute())
        {
            return;
        }

        // Told, not silently dropped: a tell that vanishes reads as a bug, and a pest who thinks
        // the game is broken keeps trying. Checked after the mute so a muted player is told about
        // the mute, which is the thing that is actually stopping them.
        if (target.Ignores(ctx.Actor))
        {
            ctx.Reply($"{target.Name} is not listening to you.", "bad");
            return;
        }

        ctx.Reply($"You tell {target.Name}, '{message}'", "tell");
        target.SendText($"{ctx.Actor.TaggedName} tells you, '{message}'", "tell");

        // Set even when the recipient is link-dead: they may well reconnect inside the grace
        // window, and a reply target that quietly failed to update would answer the wrong person.
        target.LastTellFrom = ctx.Actor.CharacterId;

        if (target.IsLinkDead)
        {
            // §3.6 leaves a link-dead character standing in the room, so they look present.
            // Saying so costs the sender nothing and stops them waiting on an answer.
            ctx.Reply($"{target.Name} seems to be somewhere else entirely.", "bad");
        }
    }

    private static void Chat(CommandContext ctx)
    {
        var argument = ctx.Argument.Trim();

        // "chat off" turns the channel off rather than posting the word "off". The convention is
        // old enough to be what players expect, and the cost is that one two-letter message has to
        // be phrased differently.
        if (argument.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Actor.ChatOff = true;
            ctx.Reply("The world channel goes quiet.", "chat");
            return;
        }

        if (argument.Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Actor.ChatOff = false;
            ctx.Reply("You are listening to the world channel again.", "chat");
            return;
        }

        if (argument.Length == 0)
        {
            ctx.Reply(
                ctx.Actor.ChatOff
                    ? "The world channel is off. 'chat on' to hear it."
                    : "Chat what? ('chat off' to stop hearing it.)",
                "bad");
            return;
        }

        if (ctx.Actor.ChatOff)
        {
            ctx.Reply("Your world channel is off. 'chat on' first.", "bad");
            return;
        }

        // Checked after the on/off branches, so a muted player can still turn the channel off and
        // back on — the mute is about what they send, not about what reaches them.
        if (ctx.RefusedForMute())
        {
            return;
        }

        foreach (var player in ctx.World.AllPlayers)
        {
            if (player.ChatOff || player.Ignores(ctx.Actor))
            {
                continue;
            }

            player.SendText(
                player.CharacterId == ctx.Actor.CharacterId
                    ? $"You chat, '{argument}'"
                    : $"{ctx.Actor.TaggedName} chats, '{argument}'",
                "chat");
        }
    }

    /// <summary>Splits "kael hello there" into the name and the rest of the line.</summary>
}
