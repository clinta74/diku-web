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
