using DikuWeb.Engine.Social;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Commands;

/// <summary>
/// Forming and running a party (PLAN.md §5.3).
/// </summary>
/// <remarks>
/// One verb with subcommands rather than six top-level verbs. Grouping is occasional - you form
/// a party once and then play - so six single-letter-ambiguous verbs would cost every other
/// command a prefix for something typed twice an hour.
/// </remarks>
public static class PartyCommands
{
    public static void Register(List<CommandDefinition> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        // "gr", not "g": get and give are registered first and both answer to a single letter.
        commands.Add(new CommandDefinition(
            "group",
            2,
            "group [invite|accept|decline|leave|kick|disband] <who> (gr) - adventure together",
            Group));

        commands.Add(new CommandDefinition(
            "gtell", 2, "gtell <message> (gt) - speak to your group, wherever they are", GroupTell));

        // Full word. "a", "ab" and "as" are already spoken for by abilities and assist, and there
        // is nothing to be gained from a short form for a verb typed once a trip.
        commands.Add(new CommandDefinition(
            "autofollow",
            4,
            "autofollow <player> (auto) - walk behind someone in your group",
            AutoFollow));
    }

    /// <summary>
    /// Turns following somebody in your group on, or off again (PLAN.md §4.17).
    /// </summary>
    /// <remarks>
    /// A toggle on the <em>target</em>: naming the person you are already following stops it, and
    /// naming somebody else moves the follow rather than stacking a second one, because following
    /// two people at once has no meaning the moment they part.
    /// </remarks>
    private static void AutoFollow(CommandContext ctx)
    {
        var actor = ctx.Actor;
        var world = ctx.World;

        // Bare `autofollow` stops, which is what a player reaches for when they want out and are
        // not sure they remember whose name they typed.
        if (!ctx.HasArgument)
        {
            ctx.Reply(
                world.StopFollowing(actor.CharacterId)
                    ? "You stop following."
                    : "You are not following anyone.");
            return;
        }

        var target = NameMatch.Best(
                world.OthersIn(actor.RoomKey, actor), ctx.Argument, p => p.Name, _ => null)
            ?? NameMatch.Best(
                world.AllPlayers.Where(p => p.CharacterId != actor.CharacterId).ToList(),
                ctx.Argument, p => p.Name, _ => null);

        if (target is null || target.CharacterId == actor.CharacterId)
        {
            ctx.Reply($"You don't see '{ctx.Argument}' here.");
            return;
        }

        if (world.LeaderOf(actor.CharacterId) == target.CharacterId)
        {
            world.StopFollowing(actor.CharacterId);
            ctx.Reply($"You stop following {target.Name}.");
            return;
        }

        // The group is where the consent to be dragged around the world already lives. A bare name
        // in a room does not establish it, and following is a standing licence rather than a step.
        if (!world.Parties.SameParty(actor.CharacterId, target.CharacterId))
        {
            ctx.Reply($"{target.Name} is not in your group.", "bad");
            return;
        }

        if (Circles(world, actor.CharacterId, target.CharacterId) is { } circle)
        {
            ctx.Reply(circle, "bad");
            return;
        }

        world.SetFollowing(actor.CharacterId, target.CharacterId);
        ctx.Reply($"You begin following {target.Name}.");
        target.SendText($"{actor.Name} begins following you.", "party");
    }

    /// <summary>
    /// Why following <paramref name="leaderId"/> would go round in a circle, or null.
    /// </summary>
    /// <remarks>
    /// Refused when the verb is typed rather than only guarded at propagation, because a player who
    /// is told <em>now</em> can fix it now — and the two of them standing still while each waits for
    /// the other to move first is not a state anything on screen would explain.
    /// </remarks>
    private static string? Circles(WorldState world, Guid followerId, Guid leaderId)
    {
        if (world.LeaderOf(leaderId) == followerId)
        {
            return $"{world.FindByCharacter(leaderId)?.Name ?? "They"} is already following you.";
        }

        // Longer rings: A follows B follows C follows A. Walked rather than assumed away, because
        // a three-person party is enough to build one and nothing else would catch it.
        var seen = new HashSet<Guid> { followerId };
        var walk = world.LeaderOf(leaderId);

        while (walk is { } next)
        {
            if (next == followerId)
            {
                return "That would leave your group going round in a circle.";
            }

            if (!seen.Add(next))
            {
                break;
            }

            walk = world.LeaderOf(next);
        }

        return null;
    }

    private static void Group(CommandContext ctx)
    {
        // Lowercased: a subcommand is matched against a fixed vocabulary.
        var (subcommand, rest) = CommandText.SplitFirstWord(ctx.Argument, lowercase: true);

        switch (subcommand)
        {
            case "":
                ShowRoster(ctx);
                return;

            case "invite":
                Invite(ctx, rest);
                return;

            case "accept":
            case "join":
                Accept(ctx);
                return;

            case "decline":
            case "refuse":
                Decline(ctx);
                return;

            case "leave":
                Leave(ctx);
                return;

            case "kick":
            case "remove":
                Kick(ctx, rest);
                return;

            case "disband":
                Disband(ctx);
                return;

            default:
                ctx.Reply(
                    "group, or group invite <who>, accept, decline, leave, kick <who>, disband.",
                    "bad");
                return;
        }
    }

    private static void ShowRoster(CommandContext ctx)
    {
        var world = ctx.World;
        var party = world.Parties.Of(ctx.Actor.CharacterId);

        if (party is null)
        {
            // The pending invitation is shown here rather than only when it arrives, because the
            // message announcing it scrolls away and the offer outlives the scrollback.
            var invitation = world.Parties.InvitationFor(
                ctx.Actor.CharacterId, ctx.Clock?.CurrentPulse ?? 0L);

            if (invitation is not null &&
                world.FindByCharacter(invitation.InviterId) is { } inviter)
            {
                ctx.Reply(
                    $"{inviter.Name} has asked you to join their group. " +
                    "'group accept' or 'group decline'.",
                    "party");
                return;
            }

            ctx.Reply("You are not in a group.", "bad");
            return;
        }

        ctx.Reply($"Your group ({party.Count} of {Party.MaxMembers}):", "heading");

        foreach (var memberId in party.Members)
        {
            if (world.FindByCharacter(memberId) is not { } member)
            {
                continue;
            }

            var lead = party.IsLeader(memberId) ? " (leader)" : string.Empty;
            var away = member.RoomKey == ctx.Actor.RoomKey ? string.Empty : " — elsewhere";
            var linkDead = member.IsLinkDead ? " [link-dead]" : string.Empty;
            var vitals = member.Character.Vitals;

            ctx.Reply(
                $"  {member.Name}{lead}  level {member.Character.Level} {member.Character.Path}  " +
                $"{vitals.Health}/{vitals.HealthMax} hp{away}{linkDead}",
                "party");
        }
    }

    private static void Invite(CommandContext ctx, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            ctx.Reply("Invite whom?", "bad");
            return;
        }

        var world = ctx.World;
        var party = world.Parties.Of(ctx.Actor.CharacterId);

        if (party is not null && !party.IsLeader(ctx.Actor.CharacterId))
        {
            ctx.Reply("Only the group's leader may invite.", "bad");
            return;
        }

        if (party is not null && party.IsFull)
        {
            ctx.Reply($"A group holds {Party.MaxMembers}. Yours is full.", "bad");
            return;
        }

        // Named from the room rather than the whole world: an invitation is something you say to
        // someone standing in front of you, and inviting by name across the map would be a way to
        // reach a stranger who has no way to stop you.
        var target = NameMatch.Best(
            world.OthersIn(ctx.Actor.RoomKey, ctx.Actor), name, p => p.Name, _ => null);

        if (target is null)
        {
            ctx.Reply($"You don't see '{name}' here.", "bad");
            return;
        }

        if (world.Parties.IsGrouped(target.CharacterId))
        {
            ctx.Reply($"{target.Name} is already in a group.", "bad");
            return;
        }

        world.Parties.Invite(
            ctx.Actor.CharacterId, target.CharacterId, ctx.Clock?.CurrentPulse ?? 0L);

        ctx.Reply($"You ask {target.Name} to join your group.", "party");
        target.SendText(
            $"{ctx.Actor.Name} asks you to join their group. 'group accept' or 'group decline'.",
            "party");
    }

    private static void Accept(CommandContext ctx)
    {
        var world = ctx.World;
        var pulse = ctx.Clock?.CurrentPulse ?? 0L;

        var result = world.Parties.Accept(
            ctx.Actor.CharacterId,
            id => world.FindByCharacter(id) is not null,
            pulse);

        switch (result)
        {
            case PartyJoinResult.NoInvitation:
                ctx.Reply("Nobody has asked you to join a group.", "bad");
                return;

            case PartyJoinResult.AlreadyGrouped:
                ctx.Reply("You are already in a group. Leave it first.", "bad");
                return;

            case PartyJoinResult.InviterGone:
                ctx.Reply("Whoever asked you is no longer here.", "bad");
                return;

            case PartyJoinResult.Full:
                ctx.Reply("That group filled up while you were deciding.", "bad");
                return;
        }

        // The inviter is told through the roster rather than separately: by now they are in the
        // same party, so one announcement reaches them and everyone who was already in it.
        ctx.Reply("You join the group.", "party");
        Announce(world, ctx.Actor.CharacterId, $"{ctx.Actor.Name} joins the group.", skipSelf: true);
    }

    private static void Decline(CommandContext ctx)
    {
        var world = ctx.World;
        var invitation = world.Parties.InvitationFor(
            ctx.Actor.CharacterId, ctx.Clock?.CurrentPulse ?? 0L);

        if (invitation is null)
        {
            ctx.Reply("Nobody has asked you to join a group.", "bad");
            return;
        }

        world.Parties.Decline(ctx.Actor.CharacterId);
        ctx.Reply("You decline.", "party");

        world.FindByCharacter(invitation.InviterId)
            ?.SendText($"{ctx.Actor.Name} declines your invitation.", "party");
    }

    private static void Leave(CommandContext ctx)
    {
        var world = ctx.World;

        if (world.Parties.Of(ctx.Actor.CharacterId) is null)
        {
            ctx.Reply("You are not in a group.", "bad");
            return;
        }

        // Announced before leaving, so the person leaving is still in the roster being told.
        Announce(world, ctx.Actor.CharacterId, $"{ctx.Actor.Name} leaves the group.", skipSelf: true);

        var party = world.Parties.Leave(ctx.Actor.CharacterId);
        ctx.Reply("You leave the group.", "party");

        NotifyIfDissolved(world, party);
    }

    private static void Kick(CommandContext ctx, string name)
    {
        var world = ctx.World;
        var party = world.Parties.Of(ctx.Actor.CharacterId);

        if (party is null)
        {
            ctx.Reply("You are not in a group.", "bad");
            return;
        }

        if (!party.IsLeader(ctx.Actor.CharacterId))
        {
            ctx.Reply("Only the group's leader may remove someone.", "bad");
            return;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            ctx.Reply("Remove whom?", "bad");
            return;
        }

        // Searched among members wherever they are, not in the room - the member you most want to
        // remove is the one who wandered off.
        var members = party.Members
            .Where(id => id != ctx.Actor.CharacterId)
            .Select(world.FindByCharacter)
            .OfType<PlayerActor>()
            .ToList();

        var target = NameMatch.Best(members, name, p => p.Name, _ => null);

        if (target is null)
        {
            ctx.Reply($"'{name}' is not in your group.", "bad");
            return;
        }

        // Removed first, so the announcement does not reach the person it is about - they are
        // told in the second person instead.
        var left = world.Parties.Leave(target.CharacterId);

        target.SendText("You are removed from the group.", "bad");
        ctx.Reply($"You remove {target.Name} from the group.", "party");
        Announce(world, ctx.Actor.CharacterId, $"{target.Name} is removed from the group.", skipSelf: true);

        NotifyIfDissolved(world, left);
    }

    private static void Disband(CommandContext ctx)
    {
        var world = ctx.World;
        var party = world.Parties.Of(ctx.Actor.CharacterId);

        if (party is null)
        {
            ctx.Reply("You are not in a group.", "bad");
            return;
        }

        if (!party.IsLeader(ctx.Actor.CharacterId))
        {
            ctx.Reply("Only the group's leader may disband it. Try 'group leave'.", "bad");
            return;
        }

        foreach (var memberId in world.Parties.Disband(party))
        {
            world.FindByCharacter(memberId)?.SendText("The group disbands.", "party");
        }
    }

    private static void GroupTell(CommandContext ctx)
    {
        if (!ctx.HasArgument)
        {
            ctx.Reply("Tell your group what?", "bad");
            return;
        }

        var world = ctx.World;
        var party = world.Parties.Of(ctx.Actor.CharacterId);

        if (party is null)
        {
            ctx.Reply("You are not in a group.", "bad");
            return;
        }

        // A mute covers the party channel too. It reaches other players, which is the whole of
        // what a mute is about - and a silenced player with a private channel to five people is
        // not silenced.
        if (ctx.RefusedForMute())
        {
            return;
        }

        // Reaches the whole party wherever they are - which is the point of having one channel
        // that is not the room.
        foreach (var memberId in party.Members)
        {
            if (world.FindByCharacter(memberId) is not { } member)
            {
                continue;
            }

            member.SendText(
                memberId == ctx.Actor.CharacterId
                    ? $"You tell the group, '{ctx.Argument}'"
                    : $"{ctx.Actor.Name} tells the group, '{ctx.Argument}'",
                "party");
        }
    }

    /// <summary>Sends a line to everyone in this character's party.</summary>
    internal static void Announce(
        WorldState world,
        Guid characterId,
        string text,
        bool skipSelf)
    {
        ArgumentNullException.ThrowIfNull(world);

        if (world.Parties.Of(characterId) is not { } party)
        {
            return;
        }

        foreach (var memberId in party.Members)
        {
            if (skipSelf && memberId == characterId)
            {
                continue;
            }

            world.FindByCharacter(memberId)?.SendText(text, "party");
        }
    }

    /// <summary>
    /// Tells the last member standing that the group is over.
    /// </summary>
    /// <remarks>
    /// A party of one dissolves itself (<see cref="Party.Remove"/>), and the person left behind
    /// would otherwise find out by noticing their group commands had started refusing.
    /// </remarks>
    private static void NotifyIfDissolved(WorldState world, Party? party)
    {
        if (party is null || party.Count != 1)
        {
            return;
        }

        world.FindByCharacter(party.Members[0])
            ?.SendText("You are the last one left. The group breaks up.", "party");
    }

}
