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

    /// <summary>The name of the session cookie.</summary>
    /// <remarks>
    /// Settable because the shipped compose files have always set it. It bound to nothing: the
    /// name was a literal at the call site, so every deployment that believed it had chosen one
    /// was running the default, and nothing said otherwise.
    ///
    /// Worth being configuration on its own merits. Two deployments on sibling hosts under one
    /// parent domain overwrite each other's cookie unless they can be told apart, and that is a
    /// deploy-time fact the build cannot know.
    /// </remarks>
    public string CookieName { get; set; } = "muwbta.session";

    /// <summary>How long a session survives without use, in minutes.</summary>
    /// <remarks>
    /// Ignored until now as well, and more quietly: the compose files set 20160, the code used a
    /// hardcoded fourteen days, and those are the same number. The key looked like it worked and
    /// would have gone on looking that way until somebody changed it.
    ///
    /// Sliding, so this is time since last use rather than since sign-in. A fortnight is long for
    /// a session and deliberately so: the alternative is signing out a player who missed a week,
    /// to protect an account that holds no payment details.
    /// </remarks>
    public int SessionTimeoutMinutes { get; set; } = 20160;

    /// <summary>Floored at a minute: a zero here would expire the cookie that set it.</summary>
    public TimeSpan SessionTimeout => TimeSpan.FromMinutes(Math.Max(1, SessionTimeoutMinutes));

    /// <summary>
    /// Wrong passwords against one account before the next attempt has to wait. Zero turns the
    /// per-account backoff off.
    /// </summary>
    /// <remarks>
    /// Five is the number a person who has genuinely forgotten reaches before the pause is a
    /// nuisance, and the number a guesser passes in the first second. See
    /// <see cref="LoginThrottle"/> for why this exists beside the per-address limit.
    /// </remarks>
    public int LoginFailuresBeforeBackoff { get; set; } = 5;

    /// <summary>The first pause, in seconds. Each further failure doubles it.</summary>
    public int LoginBackoffSeconds { get; set; } = 30;

    /// <summary>
    /// The longest pause, in seconds. The cap is what stops "hammer an account" from becoming
    /// "own an account": without it the real owner could never get back in.
    /// </summary>
    public int LoginBackoffMaxSeconds { get; set; } = 900;
}
