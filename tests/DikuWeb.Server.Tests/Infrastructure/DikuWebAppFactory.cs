using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

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
    }
}
