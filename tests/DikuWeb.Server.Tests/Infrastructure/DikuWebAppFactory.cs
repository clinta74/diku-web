using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DikuWeb.Server.Tests.Infrastructure;

/// <summary>
/// Boots the real server against the containerised database. Nothing is stubbed: the
/// health endpoints under test are the ones that will run in production.
/// </summary>
public sealed class DikuWebAppFactory(string connectionString) : WebApplicationFactory<Program>
{
    /// <summary>
    /// Small on purpose. Each test builds its own host, and a host's pool is not torn down the
    /// instant the factory is disposed, so several pools briefly overlap. Npgsql defaults to
    /// 100 connections per pool and the server's own cap is only applied on the
    /// DatabaseConnection:* configuration path, which tests do not use - so without this the
    /// overlap exhausted Postgres and tests failed with "53300: sorry, too many clients
    /// already". A single-threaded test needs a handful of connections at most.
    /// </summary>
    private const int TestMaxPoolSize = 8;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // "Testing" rather than "Development" so appsettings.Development.json - which points
        // at the docker-compose database - cannot leak into a test run.
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DikuWeb", CapPool(connectionString));

        // Revalidate the principal on every authenticated request rather than once a minute
        // (PLAN.md §7.7). A test that promoted somebody and then immediately checked their
        // access would otherwise be asserting the cache, not the behaviour - and would pass
        // for the wrong reason if revalidation were removed entirely.
        builder.UseSetting("Auth:RevalidationIntervalSeconds", "0");

        // No wait for the database. Production spends up to a minute riding out a Postgres that
        // is still coming up; a test pointed at an unreachable host should fail in a second, and
        // a test pointed at a live container has nothing to wait for.
        builder.UseSetting("Database:MigrationRetryBudgetSeconds", "0");

        builder.ConfigureLogging(logging =>
        {
            // Drop the host's default providers, the Windows Event Log one in particular.
            //
            // Every test builds its own host in the same process, and EventLogLoggerProvider
            // wraps a handle that the first host to be disposed closes for everyone. Any later
            // host that logs a warning then throws ObjectDisposedException from inside
            // ILogger.Log - which surfaces as a failure in whichever test happened to be
            // making the request, with a stack trace pointing at logging rather than at
            // anything the test did. The readiness-check test is the usual victim, because
            // failing a health check is the most reliable way to log a warning.
            //
            // Tests also have no business writing to the machine's event log.
            logging.ClearProviders();
            logging.AddDebug();
        });
    }

    private static string CapPool(string raw) =>
        new NpgsqlConnectionStringBuilder(raw)
        {
            MaxPoolSize = TestMaxPoolSize,

            // Hand connections back promptly rather than parking them for the default five
            // minutes, so a finished test's pool stops holding slots the next one needs.
            ConnectionIdleLifetime = 5,
            ConnectionPruningInterval = 1,
        }.ConnectionString;
}
