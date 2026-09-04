using System.Net.Http.Json;
using Muwbta.Server.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Muwbta.Server.Tests;

/// <summary>
/// The security counters reach the scrape under the names the dashboard queries.
/// </summary>
/// <remarks>
/// The counters are unit-tested against a listener; this proves the last hop, which is the one
/// that fails quietly: a meter the exporter does not subscribe to, or a name that Prometheus
/// spells differently, produces a panel that is simply empty. A counter with no observations is
/// not exported at all, so each one is nudged before the fetch.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class SecurityMetricsScrapeTests(PostgresFixture postgres) : IDisposable
{
    /// <summary>Its own host, for the reason given on MetricsEndpointTests.</summary>
    private readonly MuwbtaAppFactory _factory = new(postgres.ConnectionString);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task A_refused_sign_in_and_a_request_both_show_up_in_the_scrape()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        // One unknown name, one registration: enough for both counters to exist.
        await client.PostAsJsonAsync("/api/auth/login", new { username = "nobody-scrape", password = "guess" });
        await BuilderClient.RegisterAsync(client);

        var body = await client.GetStringAsync(new Uri("/metrics", UriKind.Relative));

        // The counter, under its Prometheus spelling, with the tag the dashboard slices on.
        Assert.Contains("muwbta_signins_total{", body, StringComparison.Ordinal);
        Assert.Contains("outcome=\"unknown_user\"", body, StringComparison.Ordinal);
        Assert.Contains("muwbta_registrations_total{", body, StringComparison.Ordinal);
        Assert.Contains("outcome=\"created\"", body, StringComparison.Ordinal);

        // The framework's request histogram, which the HTTP-error panels query by this name and
        // this label. Nothing in this repository defines either, so this is the only place that
        // would notice the framework renaming them.
        Assert.Contains("http_server_request_duration_seconds_count{", body, StringComparison.Ordinal);
        Assert.Contains("http_response_status_code=\"", body, StringComparison.Ordinal);
    }
}
