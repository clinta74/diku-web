using System.Globalization;
using System.Threading.RateLimiting;
using DikuWeb.Server.Auth;
using Microsoft.AspNetCore.RateLimiting;

namespace DikuWeb.Server.Infrastructure;

/// <summary>
/// Per-caller request limits (PLAN.md §8, Phase 6, and the §10 risk table).
/// </summary>
/// <remarks>
/// <b>What this is not.</b> It is not a defence against a distributed flood — that belongs at the
/// reverse proxy, which sees the connection before ASP.NET Core allocates anything for it. What it
/// is: a bound on what a <em>single authenticated caller</em> can cost the one process that owns
/// the world. The game loop is a single thread (§2.1), so a player who can enqueue faster than it
/// drains is not just hurting themselves.
///
/// Three policies, because the three surfaces have genuinely different shapes:
/// <list type="bullet">
/// <item><description><b>Commands</b> — partitioned by character, since that is the thing the loop
/// spends its budget on. Generous enough that fast typing and a held-down movement key never
/// notice.</description></item>
/// <item><description><b>Builder</b> — partitioned by account, and deliberately looser: the tree
/// view issues a burst of reads on load, and a builder is a trusted role whose failure mode is
/// mess rather than breach (the same reasoning <see cref="Building.DigThrottle"/> already
/// carries).</description></item>
/// <item><description><b>Auth</b> — partitioned by remote address, because the caller is by
/// definition not yet authenticated. This is the only one of the three defending against a
/// stranger, and the only one where the limit is tight.</description></item>
/// </list>
///
/// Every limiter is in-process, which is exactly right here and would not be if this scaled out:
/// a single-writer loop cannot run two instances anyway (§6.1), so there is no second process for
/// a shared counter to coordinate with.
/// </remarks>
public static class RateLimiting
{
    /// <summary>Named so an endpoint group asks for a policy rather than restating its numbers.</summary>
    public const string Commands = "commands";

    /// <inheritdoc cref="Commands"/>
    public const string Builder = "builder";

    /// <inheritdoc cref="Commands"/>
    public const string Auth = "auth";

    /// <summary>
    /// The builder assist. Same trusted role as <see cref="Builder"/>, completely different budget.
    /// </summary>
    /// <remarks>
    /// Every builder read is free and local; one assist call occupies the only model this server
    /// has for about three minutes. A share of the builder policy's 120-burst would let one person
    /// fill the queue and hold it for half an hour, so this is a separate policy with a number that
    /// reflects what the work costs rather than what the request costs.
    /// </remarks>
    public const string Assist = "assist";

    /// <summary>
    /// The numbers, so a deployment can loosen or tighten them without a rebuild.
    /// </summary>
    /// <remarks>
    /// Configurable because the right values depend on things this code cannot see: how many
    /// players share an address behind one NAT, whether a load generator is pointed at a staging
    /// box, and — for the test suite — that one host serves every test in the collection from a
    /// single loopback address, which is the sharpest version of the shared-address problem.
    /// </remarks>
    public sealed class Options
    {
        public int CommandBurst { get; set; } = 20;
        public int CommandsPerSecond { get; set; } = 5;
        public int BuilderBurst { get; set; } = 120;
        public int BuilderPerSecond { get; set; } = 20;
        public int AuthAttemptsPerMinute { get; set; } = 10;

        /// <summary>Assist requests per account per minute. Six is twice the queue's own depth.</summary>
        public int AssistRequestsPerMinute { get; set; } = 6;
    }

    public static IServiceCollection AddDikuWebRateLimiting(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var limits = new Options();
        configuration?.GetSection("RateLimits").Bind(limits);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Told how long to wait rather than left to guess. A client that retries immediately
            // on a 429 turns one limit breach into a tight loop, which is the thing being limited.
            options.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds))
                            .ToString(NumberFormatInfo.InvariantInfo);
                }

                return ValueTask.CompletedTask;
            };

            // A burst of twenty and then five a second. Human typing peaks around two or three,
            // and a held-down direction key is the realistic worst case for someone playing
            // honestly - both sit far below this, so the limit is invisible until it is being
            // abused. Queue depth zero: a command that has waited is worse than one refused,
            // because the player has already typed the next three.
            options.AddPolicy(Commands, http => RateLimitPartition.GetTokenBucketLimiter(
                CharacterKey(http),
                _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = limits.CommandBurst,
                    TokensPerPeriod = limits.CommandsPerSecond,
                    ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));

            // Looser, and by account rather than by character: opening the builder issues a burst
            // of reads for the zone tree, and a builder editing quickly is doing their job.
            options.AddPolicy(Builder, http => RateLimitPartition.GetTokenBucketLimiter(
                AccountKey(http),
                _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = limits.BuilderBurst,
                    TokensPerPeriod = limits.BuilderPerSecond,
                    ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));

            // The tight one, and the only one defending against a stranger. Ten attempts a minute
            // per address is far more than a person needs and far less than a password guesser
            // wants. A fixed window rather than a bucket, because "ten tries a minute" is the
            // sentence a human would write in a policy and there is no burst worth allowing.
            //
            // The weakness worth naming: behind a reverse proxy, every caller shares the proxy's
            // address unless forwarded headers are honoured — which would turn this into a global
            // cap of ten sign-ins a minute for the whole site. See the deployment note in
            // Program.cs; the numbers are configurable for exactly this reason.
            // Per account, per minute, in a fixed window - the same shape as Auth and for the same
            // reason: "six an hour's worth of work" is a sentence about a budget, and a token
            // bucket's burst is exactly what should not be allowed here.
            options.AddPolicy(Assist, http => RateLimitPartition.GetFixedWindowLimiter(
                AccountKey(http),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limits.AssistRequestsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));

            options.AddPolicy(Auth, http => RateLimitPartition.GetFixedWindowLimiter(
                AddressKey(http),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limits.AuthAttemptsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));
        });

        return services;
    }

    /// <summary>
    /// The character this request is about, falling back to the account and then the address.
    /// </summary>
    /// <remarks>
    /// By character rather than by account, because the loop's budget is spent per character in
    /// the world — an account with three characters playing legitimately costs three characters'
    /// worth of work, and should be allowed to.
    /// </remarks>
    private static string CharacterKey(HttpContext http)
    {
        var character = http.Request.RouteValues.TryGetValue("characterId", out var value)
            ? value?.ToString()
            : null;

        return character is null ? AccountKey(http) : $"character:{character}";
    }

    /// <summary>
    /// The signed-in account, or the remote address when there is not one.
    /// </summary>
    /// <remarks>
    /// The fallback matters: partitioning every anonymous request into one shared bucket would let
    /// a single stranger exhaust the limit for all of them, turning a rate limit into an outage.
    /// </remarks>
    private static string AccountKey(HttpContext http) =>
        http.TryGetAccountId(out var accountId)
            ? $"account:{accountId}"
            : AddressKey(http);

    private static string AddressKey(HttpContext http) =>
        $"address:{http.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
}
