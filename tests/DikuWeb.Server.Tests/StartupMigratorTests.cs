using DikuWeb.Server.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace DikuWeb.Server.Tests;

/// <summary>
/// The retry policy, driven through a fake migration step and real millisecond delays.
/// </summary>
/// <remarks>
/// A fake clock was the obvious choice here and the wrong one: driving it means advancing time
/// from a loop that cannot see whether the code under test has reached its <c>Task.Delay</c>
/// yet, so the driver races the retry and can burn the whole budget before the database "comes
/// up". Shrinking the delays instead exercises the real timer, deterministically, in under a
/// millisecond of waiting.
/// </remarks>
public sealed class StartupMigratorTests
{
    /// <summary>Real delays, small enough that the whole suite costs a few milliseconds.</summary>
    private static StartupMigrator.RetryPolicy Policy(double budgetMs) =>
        new(TimeSpan.FromMilliseconds(budgetMs),
            TimeSpan.FromMilliseconds(1),
            TimeSpan.FromMilliseconds(2));

    /// <summary>A connection failure - what a database that is still starting up looks like.</summary>
    private static NpgsqlException Transient() =>
        new("Failed to connect", new TimeoutException());

    private static Task Run(Func<CancellationToken, Task> migrate, StartupMigrator.RetryPolicy policy) =>
        StartupMigrator.RunAsync(migrate, policy, NullLogger.Instance, TimeProvider.System);

    [Fact]
    public async Task A_first_attempt_that_succeeds_is_the_only_attempt()
    {
        var attempts = 0;

        await Run(_ => { attempts++; return Task.CompletedTask; }, Policy(budgetMs: 5000));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task A_database_that_comes_up_late_is_waited_out()
    {
        // The budget is enormous relative to the delays, so the only thing under test is that
        // the retry happens at all - not how fast the machine running it happens to be.
        var attempts = 0;

        await Run(_ => ++attempts < 4 ? Task.FromException(Transient()) : Task.CompletedTask,
            Policy(budgetMs: 5000));

        Assert.Equal(4, attempts);
    }

    [Fact]
    public async Task A_failure_that_is_not_transient_is_thrown_immediately()
    {
        // A wrong password must not cost a minute of retries before anyone is told.
        var attempts = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Run(_ =>
            {
                attempts++;
                return Task.FromException(
                    new InvalidOperationException("28P01: password authentication failed"));
            },
            Policy(budgetMs: 5000)));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task The_last_failure_surfaces_once_the_budget_runs_out()
    {
        // Rethrown as-is rather than wrapped, so the crash log names the real cause.
        var attempts = 0;

        var thrown = await Assert.ThrowsAsync<NpgsqlException>(() =>
            Run(_ => { attempts++; return Task.FromException(Transient()); }, Policy(budgetMs: 200)));

        Assert.Equal("Failed to connect", thrown.Message);
        Assert.True(attempts > 1, $"The budget should have allowed more than one attempt, saw {attempts}.");
    }

    [Fact]
    public async Task A_zero_budget_makes_even_a_transient_failure_fatal()
    {
        // What the test host uses, so a host pointed at an unreachable database fails in a
        // second rather than sixty.
        var attempts = 0;

        await Assert.ThrowsAsync<NpgsqlException>(() =>
            Run(_ => { attempts++; return Task.FromException(Transient()); },
                StartupMigrator.RetryPolicy.For(TimeSpan.Zero)));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public void The_default_policy_backs_off_from_one_second_to_ten()
    {
        // Program.cs builds the policy this way; the shape is what keeps a slow database from
        // being hammered and a fast one from waiting ten seconds for its first retry.
        var policy = StartupMigrator.RetryPolicy.For(TimeSpan.FromSeconds(60));

        Assert.Equal(TimeSpan.FromSeconds(60), policy.Budget);
        Assert.Equal(TimeSpan.FromSeconds(1), policy.FirstDelay);
        Assert.Equal(TimeSpan.FromSeconds(10), policy.MaxDelay);
    }
}
