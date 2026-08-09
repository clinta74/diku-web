using DikuWeb.Domain.Accounts;
using DikuWeb.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DikuWeb.Server.Admin;

public enum RoleChangeOutcome
{
    Changed = 0,

    /// <summary>Already had that role. Success, but worth saying so rather than implying work.</summary>
    Unchanged = 1,

    NoSuchAccount = 2,

    /// <summary>
    /// An admin tried to reduce their own role. Refused: there is no recovery from an
    /// installation with zero admins except the SQL §7.7 exists to eliminate.
    /// </summary>
    WouldDemoteSelf = 3,
}

public sealed record RoleChangeResult(
    RoleChangeOutcome Outcome,
    string Message,
    Guid? TargetAccountId = null,
    AccountRole? Previous = null,
    AccountRole? Current = null)
{
    public bool Ok => Outcome is RoleChangeOutcome.Changed or RoleChangeOutcome.Unchanged;
}

/// <summary>
/// The outcome of a moderation action (PLAN.md §8, Phase 6).
/// </summary>
/// <param name="TargetAccountId">
/// Set only on success, and only so the worker can reach a session already open — a ban that
/// leaves the banned player connected is not a ban.
/// </param>
public sealed record ModerationResult(
    bool Ok,
    string Message,
    Guid? TargetAccountId = null,
    DateTimeOffset? MutedUntil = null)
{
    public static ModerationResult Failed(string message) => new(false, message);
}

public sealed record AccountSummary(
    Guid Id,
    string Username,
    string Email,
    string Role,
    bool IsBanned,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    IReadOnlyList<string> Characters);

/// <summary>
/// Role administration (PLAN.md §7.7). Shared by the HTTP endpoints and by the worker that
/// drains in-game <c>promote</c> commands, so both paths enforce the same rules and write the
/// same audit row - two copies of "who may do what" is how they end up disagreeing.
/// </summary>
public sealed class AccountAdminService(DikuWebDbContext db, TimeProvider clock)
{
    public async Task<RoleChangeResult> SetRoleAsync(
        Guid actorAccountId,
        string targetUsername,
        AccountRole role,
        CancellationToken cancellationToken)
    {
        // citext, so this matches however they typed it.
        var target = await db.Accounts
            .FirstOrDefaultAsync(a => a.Username == targetUsername, cancellationToken);

        if (target is null)
        {
            return new RoleChangeResult(
                RoleChangeOutcome.NoSuchAccount, $"There is no account named '{targetUsername}'.");
        }

        if (target.Id == actorAccountId && role != AccountRole.Admin)
        {
            return new RoleChangeResult(
                RoleChangeOutcome.WouldDemoteSelf,
                "You cannot reduce your own role. Have another admin do it.");
        }

        var previous = target.Role;

        if (previous == role)
        {
            return new RoleChangeResult(
                RoleChangeOutcome.Unchanged,
                $"{target.Username} is already {role}.",
                target.Id,
                previous,
                role);
        }

        target.Role = role;

        db.AdminAudits.Add(new AdminAudit
        {
            ActorAccountId = actorAccountId,
            TargetAccountId = target.Id,
            Action = AdminAction.RoleChanged,
            Before = previous.ToString(),
            After = role.ToString(),
            At = clock.GetUtcNow(),
        });

        await db.SaveChangesAsync(cancellationToken);

        return new RoleChangeResult(
            RoleChangeOutcome.Changed,
            $"{target.Username} is now {role}.",
            target.Id,
            previous,
            role);
    }

    /// <summary>
    /// Bans or lifts a ban (PLAN.md §8, Phase 6).
    /// </summary>
    /// <remarks>
    /// One method for both directions, because they are the same edit to the same column with the
    /// same audit row — a separate unban path is how one of the two ends up not writing one.
    ///
    /// Self-banning is refused for the reason self-demotion is: there is no recovery from it
    /// except the SQL §7.7 exists to eliminate. Banning <em>another</em> admin is permitted, since
    /// the alternative is an account nobody can act on.
    /// </remarks>
    public async Task<ModerationResult> SetBanAsync(
        Guid actorAccountId,
        string targetUsername,
        bool banned,
        string? reason,
        CancellationToken cancellationToken)
    {
        var target = await db.Accounts
            .FirstOrDefaultAsync(a => a.Username == targetUsername, cancellationToken);

        if (target is null)
        {
            return ModerationResult.Failed($"There is no account named '{targetUsername}'.");
        }

        if (target.Id == actorAccountId)
        {
            return ModerationResult.Failed("You cannot ban yourself.");
        }

        if (target.IsBanned == banned)
        {
            return ModerationResult.Failed(
                $"{target.Username} is already {(banned ? "banned" : "not banned")}.");
        }

        target.IsBanned = banned;
        target.BanReason = banned ? reason : null;

        db.AdminAudits.Add(new AdminAudit
        {
            ActorAccountId = actorAccountId,
            TargetAccountId = target.Id,
            Action = banned ? AdminAction.Banned : AdminAction.Unbanned,
            Before = banned ? "active" : "banned",
            After = banned ? "banned" : "active",
            Reason = reason,
            At = clock.GetUtcNow(),
        });

        await db.SaveChangesAsync(cancellationToken);

        return new ModerationResult(
            true,
            banned
                ? $"{target.Username} is banned.{(reason is null ? string.Empty : $" ({reason})")}"
                : $"{target.Username} may sign in again.",
            target.Id);
    }

    /// <summary>
    /// Silences an account until a stated time, or lifts the silence when <paramref name="until"/>
    /// is null (PLAN.md §8, Phase 6).
    /// </summary>
    public async Task<ModerationResult> SetMuteAsync(
        Guid actorAccountId,
        string targetUsername,
        DateTimeOffset? until,
        string? reason,
        CancellationToken cancellationToken)
    {
        var target = await db.Accounts
            .FirstOrDefaultAsync(a => a.Username == targetUsername, cancellationToken);

        if (target is null)
        {
            return ModerationResult.Failed($"There is no account named '{targetUsername}'.");
        }

        if (target.Id == actorAccountId)
        {
            return ModerationResult.Failed("You cannot mute yourself.");
        }

        var before = target.MutedUntil;

        if (until is null && (before is null || before <= clock.GetUtcNow()))
        {
            return ModerationResult.Failed($"{target.Username} is not muted.");
        }

        target.MutedUntil = until;

        db.AdminAudits.Add(new AdminAudit
        {
            ActorAccountId = actorAccountId,
            TargetAccountId = target.Id,
            Action = AdminAction.MuteChanged,
            Before = before?.ToString("u") ?? "not muted",
            After = until?.ToString("u") ?? "not muted",
            Reason = reason,
            At = clock.GetUtcNow(),
        });

        await db.SaveChangesAsync(cancellationToken);

        return new ModerationResult(
            true,
            until is null
                ? $"{target.Username} may speak again."
                : $"{target.Username} is muted until {until:u}.{(reason is null ? string.Empty : $" ({reason})")}",
            target.Id,
            until);
    }

    /// <summary>
    /// Retires a character by name (PLAN.md §7.7).
    /// </summary>
    /// <remarks>
    /// <b>A soft delete.</b> The row keeps its <c>DeletedAt</c> and everything hanging off it —
    /// items, quest progress, the audit trail — stays referentially intact. Hard deletion would
    /// cascade through all of it, and "we deleted the wrong Kael" is not a recoverable sentence.
    /// The character list already filters on <c>DeletedAt == null</c>, so it disappears from the
    /// account it belonged to without anything else having to learn about deletion.
    ///
    /// <b>It does not remove them from the world.</b> That is the worker's job, because only the
    /// loop may touch a session (§2.1) — and doing it here would be exactly the race that rule
    /// exists to forbid. The name is freed either way: names are unique across live characters,
    /// and this one is no longer live.
    /// </remarks>
    public async Task<ModerationResult> DeleteCharacterAsync(
        Guid actorAccountId,
        string characterName,
        CancellationToken cancellationToken)
    {
        var character = await db.Characters.FirstOrDefaultAsync(
            c => c.Name == characterName && c.DeletedAt == null, cancellationToken);

        if (character is null)
        {
            return ModerationResult.Failed($"There is no character named '{characterName}'.");
        }

        character.DeletedAt = clock.GetUtcNow();

        db.AdminAudits.Add(new AdminAudit
        {
            ActorAccountId = actorAccountId,
            TargetAccountId = character.AccountId,
            Action = AdminAction.CharacterDeleted,

            // The name and level, because after this there is nothing left to look them up from.
            Before = $"{character.Name} (level {character.Level})",
            After = "deleted",
            At = clock.GetUtcNow(),
        });

        await db.SaveChangesAsync(cancellationToken);

        return new ModerationResult(
            true,
            $"{character.Name} has been deleted.",
            character.AccountId);
    }

    public async Task<AccountSummary?> FindAsync(string username, CancellationToken cancellationToken)
    {
        var account = await db.Accounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Username == username, cancellationToken);

        return account is null ? null : await SummariseAsync(account, cancellationToken);
    }

    public async Task<IReadOnlyList<AccountSummary>> SearchAsync(
        string? query,
        int limit,
        CancellationToken cancellationToken)
    {
        var accounts = db.Accounts.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query))
        {
            // citext makes Contains case-insensitive without a lower() wrapper, so the index
            // stays usable and the behaviour matches how usernames compare everywhere else.
            accounts = accounts.Where(a => a.Username.Contains(query));
        }

        var rows = await accounts
            .OrderBy(a => a.Username)
            .Take(Math.Clamp(limit, 1, 100))
            .ToListAsync(cancellationToken);

        var summaries = new List<AccountSummary>(rows.Count);
        foreach (var account in rows)
        {
            summaries.Add(await SummariseAsync(account, cancellationToken));
        }

        return summaries;
    }

    private async Task<AccountSummary> SummariseAsync(Account account, CancellationToken cancellationToken)
    {
        var characters = await db.Characters.AsNoTracking()
            .Where(c => c.AccountId == account.Id && c.DeletedAt == null)
            .OrderBy(c => c.Name)
            .Select(c => c.Name)
            .ToListAsync(cancellationToken);

        return new AccountSummary(
            account.Id,
            account.Username,
            account.Email,
            account.Role.ToString(),
            account.IsBanned,
            account.CreatedAt,
            account.LastLoginAt,
            characters);
    }
}
