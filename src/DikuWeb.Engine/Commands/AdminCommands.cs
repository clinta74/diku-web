using DikuWeb.Domain.Accounts;

namespace DikuWeb.Engine.Commands;

/// <summary>
/// Account administration typed in the game window (PLAN.md §7.7).
/// </summary>
/// <remarks>
/// None of these do the work themselves. Roles live in the database, and the loop is forbidden
/// from reading it (§2.1), so each validates its arguments and hands off to
/// <see cref="IAccountAdminQueue"/>. The answer arrives later as a <c>sys</c> event.
///
/// That means "your command was accepted" and "your command succeeded" are two different
/// moments here, and the wording has to be honest about which one it is reporting.
/// </remarks>
internal static class AdminCommands
{
    public static void Register(List<CommandDefinition> commands)
    {
        commands.Add(new CommandDefinition(
            "promote", 7, "promote <name> <role> - grant a role: player, builder, moderator, admin (admin)",
            Promote, Requires: AccountRole.Admin));

        commands.Add(new CommandDefinition(
            "demote", 6, "demote <name> - reduce an account to player (admin)",
            Demote, Requires: AccountRole.Admin));

        commands.Add(new CommandDefinition(
            "whois", 5, "whois <name> - account, role, and whether they are online (admin)",
            Whois, Requires: AccountRole.Admin));
    }

    private static void Promote(CommandContext ctx)
    {
        if (!RequireAdmin(ctx))
        {
            return;
        }

        var parts = ctx.Argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            ctx.Reply("Usage: promote <name> <player|builder|moderator|admin>", "bad");
            return;
        }

        // Parsed exactly, not by prefix. "promote kael a" quietly meaning Admin is the kind of
        // convenience nobody wants from this particular verb - typing the whole word is the
        // confirmation step.
        if (!TryParseRole(parts[1], out var role))
        {
            ctx.Reply(
                $"'{parts[1]}' is not a role. Use player, builder, moderator, or admin - in full.",
                "bad");
            return;
        }

        Submit(ctx, new SetAccountRoleRequest
        {
            ActorAccountId = ctx.Actor.Character.AccountId,
            ReplyToSessionId = ctx.Actor.SessionId,
            TargetUsername = parts[0],
            Role = role,
        });
    }

    private static void Demote(CommandContext ctx)
    {
        if (!RequireAdmin(ctx))
        {
            return;
        }

        if (!ctx.HasArgument)
        {
            ctx.Reply("Usage: demote <name>", "bad");
            return;
        }

        Submit(ctx, new SetAccountRoleRequest
        {
            ActorAccountId = ctx.Actor.Character.AccountId,
            ReplyToSessionId = ctx.Actor.SessionId,
            TargetUsername = ctx.Argument.Trim().Split(' ')[0],
            Role = AccountRole.Player,
        });
    }

    private static void Whois(CommandContext ctx)
    {
        if (!RequireAdmin(ctx))
        {
            return;
        }

        if (!ctx.HasArgument)
        {
            ctx.Reply("Usage: whois <name>", "bad");
            return;
        }

        Submit(ctx, new LookupAccountRequest
        {
            ActorAccountId = ctx.Actor.Character.AccountId,
            ReplyToSessionId = ctx.Actor.SessionId,
            TargetUsername = ctx.Argument.Trim().Split(' ')[0],
        });
    }

    private static void Submit(CommandContext ctx, AccountAdminRequest request)
    {
        if (ctx.AdminQueue is null)
        {
            ctx.Reply("Account administration is not available here.", "bad");
            return;
        }

        ctx.AdminQueue.Enqueue(request);
    }

    private static bool TryParseRole(string input, out AccountRole role) =>
        Enum.TryParse(input, ignoreCase: true, out role) && Enum.IsDefined(role);

    /// <summary>
    /// Admin only - a builder cannot grant roles, including to themselves. Worded as an unknown
    /// verb for everyone else, matching the builder commands (§7.6): nobody below Admin should
    /// learn from the game that these exist.
    /// </summary>
    private static bool RequireAdmin(CommandContext ctx)
    {
        if (ctx.Actor.Role == AccountRole.Admin)
        {
            return true;
        }

        ctx.Reply($"'{ctx.Verb}' is not something you can do. Try 'help'.", "bad");
        return false;
    }
}
