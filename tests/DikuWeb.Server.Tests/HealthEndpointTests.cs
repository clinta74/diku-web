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
        using var client = postgres.App.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void A_server_whose_database_is_unreachable_refuses_to_start()
    {
        // PLAN.md §6.1: migrations run at startup, so the database is a hard startup dependency
        // and a server that cannot reach it exits rather than serving. This is the contract the
        // startup-migration decision buys, and it is worth pinning down: the failure must be at
        // boot, loudly, not a process that comes up and quietly answers every request wrongly.
        //
        // It replaces an older test asserting liveness answers 200 with the database down. That
        // property still holds for an already-running process - liveness never touches the
        // database, see Liveness_returns_200 - but it can no longer be demonstrated by booting a
        // host without one, because such a host does not exist any more.
        using var factory = new DikuWebAppFactory(
            "Host=127.0.0.1;Port=1;Database=nope;Username=nope;Password=nope;Timeout=2");

        Assert.ThrowsAny<Exception>(factory.CreateClient);
    }

    [Fact]
    public async Task Readiness_reports_the_database_check()
    {
        using var client = postgres.App.CreateClient();

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
    public async Task Readiness_is_wired_to_report_unhealthy()
    {
        // The database check cannot be exercised in its failing state any more (see above), so
        // this asserts the half that is still reachable: readiness is a real aggregate that
        // reports per-check status, rather than a hardcoded 200. If the check is ever removed,
        // Readiness_reports_the_database_check fails; if the endpoint stops aggregating, this does.
        using var client = postgres.App.CreateClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        using var json = JsonDocument.Parse(body);
        var checks = json.RootElement.GetProperty("checks").EnumerateArray().ToList();

        Assert.NotEmpty(checks);
        Assert.All(checks, c => Assert.False(string.IsNullOrEmpty(c.GetProperty("status").GetString())));
    }
}
