using Npgsql;

namespace DikuWeb.Server.Infrastructure;

/// <summary>
/// Applies migrations at startup, tolerating a database that is not accepting connections yet.
/// </summary>
/// <remarks>
/// Migrations run in-process (PLAN.md §6.1), which makes the database a hard startup dependency:
/// the game loop's first act is to load the whole world, so there is nothing to serve without it.
/// Failing fast is therefore right — but failing on the *first* connection attempt is not. A
/// Postgres that is accepting TCP while still finishing recovery, or a two-second network blip,
/// would otherwise cost a container restart, and Kubernetes escalates CrashLoopBackOff to
/// five-minute delays. A ten-second hiccup should not become five minutes of downtime.
///
/// So: retry while the failure is transient and the budget lasts, then give up loudly. A wrong
/// password is not transient and still fails in about a second, which is what keeps this from
/// turning a misconfiguration into a one-minute mystery.
/// </remarks>
internal static class StartupMigrator
{
    /// <summary>
    /// How long to wait, and how the waits grow. The delays are part of the policy rather than
    /// constants so tests can shrink them to milliseconds and exercise the real timer instead of
    /// simulating one - a fake clock here would be racing the code it is meant to be driving.
    /// </summary>
    internal sealed record RetryPolicy(TimeSpan Budget, TimeSpan FirstDelay, TimeSpan MaxDelay)
    {
        public static RetryPolicy For(TimeSpan budget) =>
            new(budget, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// Runs <paramref name="migrate"/>, retrying transient failures with exponential backoff
    /// until the policy's budget is exhausted. The last failure is rethrown as-is, so the crash
    /// log names the real cause rather than a wrapper.
    /// </summary>
    /// <param name="migrate">The migration step. Injected so the retry policy is testable without a database.</param>
    public static async Task RunAsync(
        Func<CancellationToken, Task> migrate,
        RetryPolicy policy,
        ILogger logger,
        TimeProvider clock,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(migrate);
        ArgumentNullException.ThrowIfNull(policy);

        var started = clock.GetUtcNow();
        var deadline = started + policy.Budget;
        var delay = policy.FirstDelay;
        var attempt = 0;

        while (true)
        {
            attempt++;

            try
            {
                await migrate(ct);

                if (attempt > 1)
                {
                    ServerLog.DatabaseReachedAfterRetries(
                        logger, attempt, (clock.GetUtcNow() - started).TotalSeconds);
                }

                return;
            }
            catch (Exception ex) when (IsTransient(ex) && clock.GetUtcNow() + delay <= deadline)
            {
                // The guard includes the delay deliberately: sleeping past the deadline only to
                // fail on the far side of it would spend the operator's time to learn nothing.
                ServerLog.WaitingForDatabase(
                    logger,
                    delay.TotalSeconds,
                    (deadline - clock.GetUtcNow()).TotalSeconds,
                    ex.Message);

                await Task.Delay(delay, clock, ct);

                delay = delay * 2 > policy.MaxDelay ? policy.MaxDelay : delay * 2;
            }
        }
    }

    /// <summary>
    /// Whether the failure is worth waiting out. Npgsql already classifies this — including the
    /// server-side cases like 53300 "too many clients", which a restart would not fix but a few
    /// seconds might. The chain is walked because EF and the socket layer both wrap.
    /// </summary>
    private static bool IsTransient(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
        {
            if (ex is NpgsqlException { IsTransient: true })
            {
                return true;
            }
        }

        return false;
    }
}
