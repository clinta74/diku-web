using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace DikuWeb.Server.Tests.Infrastructure;

/// <summary>
/// Boots the real server against the containerised database. Nothing is stubbed: the
/// health endpoints under test are the ones that will run in production.
/// </summary>
public sealed class DikuWebAppFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // "Testing" rather than "Development" so appsettings.Development.json - which points
        // at the docker-compose database - cannot leak into a test run.
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DikuWeb", connectionString);

        // Revalidate the principal on every authenticated request rather than once a minute
        // (PLAN.md §7.7). A test that promoted somebody and then immediately checked their
        // access would otherwise be asserting the cache, not the behaviour - and would pass
        // for the wrong reason if revalidation were removed entirely.
        builder.UseSetting("Auth:RevalidationIntervalSeconds", "0");

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
}
