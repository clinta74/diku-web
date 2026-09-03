namespace Muwbta.Server.Auth;

/// <summary>Authentication tuning, bound from the <c>Auth</c> configuration section.</summary>
public sealed class AuthOptions
{
    /// <summary>
    /// How long a signed-in session may go without its role and ban state being re-checked
    /// against the database (PLAN.md §7.7).
    /// </summary>
    /// <remarks>
    /// The trade is a database read per session per interval against how stale an access
    /// decision may be. Sixty seconds is the default because role changes are rare and nobody
    /// is harmed by a promotion taking a minute to reach an open tab, while a ban that takes at
    /// most a minute is a categorically different thing from one that takes the fortnight a
    /// cookie lasts.
    ///
    /// Zero revalidates on every authenticated request. That is what the tests use, and it is a
    /// legitimate production setting for a small server that would rather pay the read.
    /// </remarks>
    public int RevalidationIntervalSeconds { get; set; } = 60;

    public TimeSpan RevalidationInterval => TimeSpan.FromSeconds(Math.Max(0, RevalidationIntervalSeconds));
}
