using System.Globalization;
using DikuWeb.Domain.Accounts;
using Microsoft.AspNetCore.Authentication;

namespace DikuWeb.Server.Auth;

/// <summary>
/// Ties an auth cookie to the password it was issued against, so changing the password
/// invalidates every ticket that predates the change (PLAN.md §7.7).
/// </summary>
/// <remarks>
/// <b>Why a stamp and not the ticket's own <c>IssuedUtc</c>.</b> Sliding expiry re-issues the
/// cookie, and a renewed ticket carries a fresh issue time — so a session older than the password
/// change would quietly acquire a timestamp newer than it and survive, which is precisely the
/// session the change existed to kill. Properties, by contrast, ride through renewal untouched.
///
/// <b>Why the value is truncated.</b> The stamp written into a cookie comes from the account in
/// memory, while the stamp it is later compared against comes back out of Postgres — and
/// <c>timestamp with time zone</c> keeps microseconds where .NET keeps 100-nanosecond ticks. Left
/// alone, the two representations of the same instant differ about nine times in ten, and the
/// session that had just changed its own password would be the first one signed out by it.
/// Truncating before the value is ever stored means both sides see the same instant.
/// </remarks>
internal static class PasswordStamp
{
    /// <summary>The authentication-properties key the stamp is stored under.</summary>
    public const string Key = "dikuweb.password";

    /// <summary>100-nanosecond ticks in the microsecond Postgres will keep.</summary>
    private const long TicksPerMicrosecond = 10;

    /// <summary>
    /// Now, at a precision that survives the round trip through the database.
    /// </summary>
    public static DateTimeOffset At(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var now = clock.GetUtcNow();
        return new DateTimeOffset(now.Ticks - (now.Ticks % TicksPerMicrosecond), now.Offset);
    }

    /// <summary>Records which password a new ticket is being issued against.</summary>
    public static void Apply(AuthenticationProperties properties, Account account)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(account);

        properties.Items[Key] = Format(account.PasswordChangedAt);
    }

    /// <summary>
    /// Whether a ticket was issued against the password the account currently has.
    /// </summary>
    /// <remarks>
    /// A cookie minted before this shipped carries no entry at all and does not match. Rejecting
    /// it costs its holder one sign-in, once; accepting it would mean the check could be skipped
    /// by presenting an older cookie, which is the whole of its value.
    /// </remarks>
    public static bool Matches(AuthenticationProperties properties, Account account)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(account);

        return properties.Items.TryGetValue(Key, out var stamp)
            && string.Equals(stamp, Format(account.PasswordChangedAt), StringComparison.Ordinal);
    }

    /// <summary>Never null, so "no stamp at all" cannot be confused with "never changed".</summary>
    private static string Format(DateTimeOffset? passwordChangedAt) =>
        passwordChangedAt?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? "initial";
}
