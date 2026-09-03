using Muwbta.Domain.Accounts;
using Muwbta.Engine;
using Muwbta.Engine.Protocol;

namespace Muwbta.Server.Admin;

/// <summary>
/// What an administrative act has to push into the running world, over and above the row it
/// writes (PLAN.md §7.7).
/// </summary>
/// <remarks>
/// Shared by the admin API and by the worker that drains in-game commands, for the same reason
/// <see cref="AccountAdminService"/> is: these are the parts that were forgotten first when there
/// were two copies. A ban applied through the panel but not the command would leave the banned
/// player in the world, and the difference would only show up in whichever path nobody tested.
///
/// Everything here goes through the gateway rather than touching a session, because only the loop
/// may do that (§2.1).
/// </remarks>
internal static class AdminLiveEffects
{
    public static void AfterRoleChange(GameGateway gateway, Guid accountId, AccountRole role) =>
        gateway.TrySubmit(new SetActorRole { AccountId = accountId, Role = role });

    /// <summary>
    /// Evicts on a ban. Cookie revalidation refuses the <em>next</em> request, but an SSE stream is
    /// one long-lived request authorised before the ban existed — so without this a banned player
    /// stays in the world until they choose to leave.
    /// </summary>
    public static void AfterBan(GameGateway gateway, Guid accountId, bool banned, string? reason)
    {
        if (!banned)
        {
            return;
        }

        gateway.TrySubmit(new EvictAccount
        {
            AccountId = accountId,
            Message = reason is null
                ? "Your account has been banned."
                : $"Your account has been banned: {reason}",
        });
    }

    /// <summary>
    /// Pushes a mute at a character already playing. The value is read at EnterWorld, so otherwise
    /// it would not reach them until they logged out — the one moment it stops mattering.
    /// </summary>
    public static void AfterMute(GameGateway gateway, Guid accountId, DateTimeOffset? mutedUntil) =>
        gateway.TrySubmit(new SetActorMute { AccountId = accountId, MutedUntil = mutedUntil });

    /// <summary>
    /// Evicts after a password is set by an administrator. Their cookie is dead at the next
    /// revalidation, but a stream opened beforehand would otherwise carry on — and this is the one
    /// case where the person holding it may not be the account's owner.
    /// </summary>
    public static void AfterPasswordReset(GameGateway gateway, Guid accountId) =>
        gateway.TrySubmit(new EvictAccount
        {
            AccountId = accountId,
            Message = "An administrator changed your password. Sign in again to continue.",
        });
}
