using System.Net;
using System.Text.Json;
using DikuWeb.Server.Tests.Infrastructure;

namespace DikuWeb.Server.Tests;

[Collection(PostgresCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class HealthEndpointTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Liveness_returns_200()
    {
        using var factory = new DikuWebAppFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Liveness_does_not_depend_on_the_database()
    {
        // PLAN.md: liveness answers "is the process up", readiness answers "can we serve".
        // Pointing at a database that does not exist must still report alive, otherwise an
        // orchestrator restarts a healthy server every time Postgres hiccups.
        using var factory = new DikuWebAppFactory(
            "Host=127.0.0.1;Port=1;Database=nope;Username=nope;Password=nope");
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Readiness_reports_the_database_check()
    {
        using var factory = new DikuWebAppFactory(postgres.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(body);
        Assert.Equal("Healthy", json.RootElement.GetProperty("status").GetString());

        var checks = json.RootElement.GetProperty("checks").EnumerateArray().ToList();
        var database = Assert.Single(checks, c => c.GetProperty("name").GetString() == "database");
        Assert.Equal("Healthy", database.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Readiness_fails_when_the_database_is_unreachable()
    {
        using var factory = new DikuWebAppFactory(
            "Host=127.0.0.1;Port=1;Database=nope;Username=nope;Password=nope;Timeout=2");
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
