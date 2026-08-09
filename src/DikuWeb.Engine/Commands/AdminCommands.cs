using System.Globalization;
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

        // Full words, both of them. These outlive the session they are typed in, which is the
        // difference between them and `kick`.
        commands.Add(new CommandDefinition(
            "ban", 3, "ban <name> [reason] - refuse an account and evict it (admin)",
            Ban, Requires: AccountRole.Admin));

        commands.Add(new CommandDefinition(
            "unban", 5, "unban <name> - let an account sign in again (admin)",
            Unban, Requires: AccountRole.Admin));

        commands.Add(new CommandDefinition(
            "mute", 4, "mute <name> <minutes> [reason] - silence an account (admin)",
            Mute, Requires: AccountRole.Admin));

        commands.Add(new CommandDefinition(
            "unmute", 6, "unmute <name> - let an account speak again (admin)",
            Unmute, Requires: AccountRole.Admin));
    }

    /// <summary>
    /// Refuses an account and throws whoever is using it out of the world.
    /// </summary>
    /// <remarks>
    /// The eviction is not optional and is not a separate command. A ban that leaves the banned
    /// player connected is not a ban — cookie revalidation (§7.7) stops their next request, but an
    /// SSE stream is one long-lived request that was authorised before the ban existed.
    /// </remarks>
    private static void Ban(CommandContext ctx)
    {
        if (!RequireAdmin(ctx))
        {
            return;
        }

        var (name, rest) = SplitName(ctx.Argument);

        if (name is null)
        {
            ctx.Reply("Usage: ban <name> [reason]", "bad");
            return;
        }

        Submit(ctx, new SetAccountBanRequest
        {
            ActorAccountId = ctx.Actor.Character.AccountId,
            ReplyToSessionId = ctx.Actor.SessionId,
            TargetUsername = name,
            Banned = true,
            Reason = rest,
        });
    }

    private static void Unban(CommandContext ctx)
    {
        if (!RequireAdmin(ctx))
        {
            return;
        }

        var (name, _) = SplitName(ctx.Argument);

        if (name is null)
        {
            ctx.Reply("Usage: unban <name>", "bad");
            return;
        }

        Submit(ctx, new SetAccountBanRequest
        {
            ActorAccountId = ctx.Actor.Character.AccountId,
            ReplyToSessionId = ctx.Actor.SessionId,
            TargetUsername = name,
            Banned = false,
        });
    }

    /// <summary>
    /// Silences an account for a stated number of minutes.
    /// </summary>
    /// <remarks>
    /// The duration is required rather than defaulted. A mute with no stated end is one somebody
    /// has to remember to lift, and the ones that get forgotten are indistinguishable from a ban
    /// nobody meant to apply.
    ///
    /// The clock used is the <em>game</em> clock, so the expiry is computed against the same time
    /// source the check reads. Two clocks is how a mute ends up lasting a different length than
    /// the one that was typed.
    /// </remarks>
    private static void Mute(CommandContext ctx)
    {
        if (!RequireAdmin(ctx))
        {
            return;
        }

        var (name, rest) = SplitName(ctx.Argument);

        if (name is null || rest is null)
        {
            ctx.Reply("Usage: mute <name> <minutes> [reason]", "bad");
            return;
        }

        var parts = rest.Split(' ', 2, StringSplitOptions.TrimEntries);

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) ||
            minutes <= 0)
        {
            ctx.Reply("How many minutes? Usage: mute <name> <minutes> [reason]", "bad");
            return;
        }

        var now = ctx.Clock?.UtcNow ?? DateTimeOffset.UtcNow;

        Submit(ctx, new SetAccountMuteRequest
        {
            ActorAccountId = ctx.Actor.Character.AccountId,
            ReplyToSessionId = ctx.Actor.SessionId,
            TargetUsername = name,
            Until = now.AddMinutes(minutes),
            Reason = parts.Length > 1 && parts[1].Length > 0 ? parts[1] : null,
        });
    }

    private static void Unmute(CommandContext ctx)
    {
        if (!RequireAdmin(ctx))
        {
            return;
        }

        var (name, _) = SplitName(ctx.Argument);

        if (name is null)
        {
            ctx.Reply("Usage: unmute <name>", "bad");
            return;
        }

        Submit(ctx, new SetAccountMuteRequest
        {
            ActorAccountId = ctx.Actor.Character.AccountId,
            ReplyToSessionId = ctx.Actor.SessionId,
            TargetUsername = name,
            Until = null,
        });
    }

    /// <summary>Splits "kael being rude" into the name and whatever followed it.</summary>
    private static (string? Name, string? Remainder) SplitName(string argument)
    {
        var trimmed = (argument ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            return (null, null);
        }

        var space = trimmed.IndexOf(' ', StringComparison.Ordinal);

        return space < 0
            ? (trimmed, null)
            : (trimmed[..space], trimmed[(space + 1)..].TrimStart() is { Length: > 0 } rest ? rest : null);
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
    internal static bool RequireAdmin(CommandContext ctx)
    {
        if (ctx.Actor.Role == AccountRole.Admin)
        {
            return true;
        }

        ctx.Reply($"'{ctx.Verb}' is not something you can do. Try 'help'.", "bad");
        return false;
    }
}
