using System.Globalization;
using System.Security.Claims;
using Muwbta.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace Muwbta.Server.Auth;

/// <summary>
/// Re-checks the signed-in account against the database periodically (PLAN.md §7.7).
/// </summary>
/// <remarks>
/// The role and the ban state are written into the auth cookie at sign-in, and a cookie lasts
/// fourteen days. Without this, changing the row changes nothing a running session can see:
///
/// <list type="bullet">
/// <item><description>A promoted builder keeps a cookie that still says Player, so the builder
/// they were just given stays closed to them until they think to log out.</description></item>
/// <item><description>A <em>demoted</em> builder keeps their access for up to two
/// weeks.</description></item>
/// <item><description>Worse, <c>is_banned</c> was only ever read at login - so banning someone
/// who was already connected did nothing at all until they chose to reconnect.</description></item>
/// <item><description>And a password change would leave every session opened with the old
/// password signed in, which is no use at all when the reason for the change is that somebody
/// else knows it (§7.8).</description></item>
/// </list>
///
/// One mechanism covers all three. The interval is the trade: a promotion taking up to a minute
/// to reach an open tab harms nobody, while a ban that takes at most a minute is a categorically
/// different thing from one that takes a fortnight. Checking on every request would be correct
/// too, and would put a database round trip on the path of every authenticated call including
/// each command POST - which is a real cost for a property that changes about once a month.
/// </remarks>
internal static class PrincipalRevalidator
{
    /// <summary>Stored in the ticket so the check is time-based rather than per-request.</summary>
    private const string LastCheckedKey = "muwbta.revalidated";

    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var services = context.HttpContext.RequestServices;
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        var interval = services.GetRequiredService<AuthOptions>().RevalidationInterval;

        if (!DueForCheck(context, now, interval))
        {
            return;
        }

        // Read from the principal rather than HttpContext.User: this runs during
        // authentication, so User is not populated yet.
        var claim = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (claim is null || !Guid.TryParse(claim, out var accountId))
        {
            await RejectAsync(context);
            return;
        }

        var factory = services.GetRequiredService<IDbContextFactory<MuwbtaDbContext>>();
        await using var db = await factory.CreateDbContextAsync(context.HttpContext.RequestAborted);
        var account = await db.Accounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == accountId, context.HttpContext.RequestAborted);

        // Deleted out from under the session, or banned since sign-in. Either way the cookie
        // stops being a credential right now rather than at its natural expiry.
        if (account is null || account.IsBanned)
        {
            await RejectAsync(context);
            return;
        }

        // Issued against a password that has since been changed - by them, or by an admin resetting
        // it for them. A password change that left the other sessions alone would not fix the case
        // it exists for, which is that somebody else knows the old one.
        if (!PasswordStamp.Matches(context.Properties, account))
        {
            await RejectAsync(context);
            return;
        }

        context.Properties.Items[LastCheckedKey] = now.ToString("O", CultureInfo.InvariantCulture);

        var currentRole = context.Principal?.FindFirst(ClaimTypes.Role)?.Value;
        if (!string.Equals(currentRole, account.Role.ToString(), StringComparison.Ordinal))
        {
            context.ReplacePrincipal(AuthEndpoints.BuildPrincipal(
                account.Id, account.Username, account.Role));
        }

        // Persists both the refreshed claims and the new check timestamp; without it the
        // timestamp is discarded and every request pays for a database read.
        context.ShouldRenew = true;
    }

    private static bool DueForCheck(
        CookieValidatePrincipalContext context,
        DateTimeOffset now,
        TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
        {
            return true;
        }

        if (!context.Properties.Items.TryGetValue(LastCheckedKey, out var raw)
            || !DateTimeOffset.TryParse(
                raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var last))
        {
            // Never checked - a cookie issued before this shipped, or one just minted at
            // sign-in. Either way, check now.
            return true;
        }

        return now - last >= interval;
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
