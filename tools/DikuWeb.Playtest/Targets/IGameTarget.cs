using DikuWeb.Domain.Accounts;

namespace DikuWeb.Playtest.Targets;

/// <summary>
/// Where the game is. Everything above this interface is mode-agnostic.
/// </summary>
/// <remarks>
/// Two implementations, and the difference between them is the whole reason this abstraction
/// exists rather than a base URL string:
///
/// <list type="bullet">
/// <item><description><b>Remote</b> — a running server. Real content, real latency, real
/// deployment. Roles can only be granted by an existing admin over the API.</description></item>
/// <item><description><b>Hosted</b> — the server booted in-process against a throwaway database.
/// Reproducible from empty, and roles can be granted by writing the row.</description></item>
/// </list>
///
/// A plan should never be able to tell which it is running against. Where it can, that is a bug in
/// the plan — usually one that assumes content somebody built by hand.
/// </remarks>
public interface IGameTarget : IAsyncDisposable
{
    /// <summary>Where the server is, for the record.</summary>
    Uri BaseAddress { get; }

    /// <summary>How this target should be described in a report.</summary>
    string Describe();

    /// <summary>
    /// A client with its own cookie jar, and therefore its own account. One per actor.
    /// </summary>
    HttpClient NewClient();

    /// <summary>
    /// Grants a role, or explains why it cannot.
    /// </summary>
    /// <remarks>
    /// Signing in again afterwards is the caller's job and is not optional: the role lands in the
    /// auth cookie as a claim at sign-in, so a promotion that skipped it leaves the client holding
    /// a cookie that still says Player. Learned the hard way in <c>BuilderClient</c>.
    /// </remarks>
    Task<PromotionResult> PromoteAsync(
        string username,
        AccountRole role,
        CancellationToken cancellationToken);
}

/// <summary>
/// Whether a role could be granted, and if not, what a plan author should do about it.
/// </summary>
/// <remarks>
/// A failure rather than an exception, because a plan needing an admin is a perfectly ordinary
/// thing to run against a server where the apparatus has no admin credential. The right answer is
/// to record that the plan could not be set up and carry on to the next one — not to end the run.
/// </remarks>
public sealed record PromotionResult(bool Granted, string? Reason)
{
    public static PromotionResult Ok { get; } = new(true, null);

    public static PromotionResult Refused(string reason) => new(false, reason);
}
