using System.Net;
using Muwbta.Engine.Telemetry;
using Muwbta.Server.Tests.Infrastructure;

namespace Muwbta.Server.Tests;

/// <summary>
/// The scrape endpoint exists, carries the engine meter, and uses the histogram buckets the
/// dashboard was drawn against (PLAN.md §8, Phase 6).
/// </summary>
/// <remarks>
/// <b>What this can and cannot catch.</b> It catches the wiring coming undone — the exporter not
/// registered, the route not mapped, the meter not subscribed, a view silently dropped — each of
/// which produces a dashboard of empty panels against a server that looks completely healthy.
///
/// It cannot catch the naming problem that actually bit while building this, and that is worth
/// writing down rather than pretending otherwise. Prometheus 3 negotiates for UTF-8 metric names
/// at scrape time and stores what the exporter calls them, so it held
/// <c>muwbta.pulse.duration_milliseconds_bucket</c> — with dots — while an ordinary HTTP fetch of
/// the same endpoint showed underscores, because a plain fetch does not send the header that asks
/// for UTF-8. The names Prometheus keeps are not the names this test sees. That gap is closed in
/// <c>tools/monitoring/prometheus.yml</c> by escaping to underscores, and nothing in .NET can
/// verify it: only running the two containers together can.
/// </remarks>
[Collection(PostgresCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class MetricsEndpointTests(PostgresFixture postgres) : IDisposable
{
    /// <summary>
    /// Its own host, and the reason is worth keeping. Every test host creates a Muwbta.Engine
    /// meter with the same instrument names, and a MeterListener is process-wide: when an
    /// earlier test disposes its host, that meter's instruments report themselves completed,
    /// and the SDK - keying by instrument identity - retires the shared host's observable
    /// gauges with them. Counters and histograms come back on their next measurement; a gauge
    /// has nothing to bring it back, so a scrape from the shared host loses rooms_loaded and
    /// sessions_active depending on what ran before. Production runs one host per process and
    /// cannot hit this. A host built here, after those teardowns, has instruments nothing has
    /// completed.
    /// </summary>
    private readonly MuwbtaAppFactory _factory = new(postgres.ConnectionString);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Metrics_endpoint_exposes_the_engine_meter()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/metrics", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await WaitForEngineSeriesAsync(client);

        // Counters with no observations are not exported at all, so the two gauges and the pulse
        // histogram are the only three that can be asserted on an idle host. That is not a gap:
        // it is the same reason the dashboard's command panels read "No data" on a quiet server.
        Assert.Contains("muwbta_pulse_duration_milliseconds", body, StringComparison.Ordinal);
        Assert.Contains("muwbta_rooms_loaded", body, StringComparison.Ordinal);
        Assert.Contains("muwbta_sessions_active", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Polls until the engine's instruments show up, or gives up loudly.
    /// </summary>
    /// <remarks>
    /// <b>Not flake insurance — the race is real and it is the interesting part.</b> An
    /// instrument that has recorded nothing is not exported, and the observable gauges do not
    /// exist until <c>PublishGauges</c> runs as the loop starts. Scraping immediately after the
    /// host is built returns a valid, complete, entirely empty response: <c>target_info</c> and
    /// nothing else. It looked exactly like a broken exporter the first time, which is worth
    /// knowing, because that is also what a genuinely broken exporter looks like — so this waits
    /// rather than retrying blind, and fails with the body it actually got.
    /// </remarks>
    private static async Task<string> WaitForEngineSeriesAsync(HttpClient client)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        var body = string.Empty;

        while (DateTimeOffset.UtcNow < deadline)
        {
            body = await client.GetStringAsync(new Uri("/metrics", UriKind.Relative));

            if (body.Contains("muwbta_pulse_duration_milliseconds", StringComparison.Ordinal))
            {
                return body;
            }

            await Task.Delay(250);
        }

        Assert.Fail(
            "The engine meter never appeared on /metrics within 15s. The endpoint answered, so " +
            $"the exporter is mapped; nothing subscribed the meter or the loop never ran. Body:\n{body}");
        return body;
    }

    [Fact]
    public async Task Pulse_histogram_keeps_its_sub_millisecond_buckets()
    {
        // The default OpenTelemetry buckets start 0, 5, 10, 25 — and a healthy pulse here is well
        // under a single millisecond, so every observation would land in the first bucket and
        // every quantile would be an interpolation inside 0–5 ms. The p99 panel would draw a
        // steady line at roughly 4.95 ms forever and look like a measurement.
        //
        // Measured on a real run: p99 was 0.21 ms and the median 0.05 ms. Without these
        // boundaries none of that is visible. Asserting the two smallest is enough to prove the
        // view is applied rather than reverted to defaults.
        using var client = _factory.CreateClient();

        var body = await WaitForEngineSeriesAsync(client);

        Assert.Contains("le=\"0.10000000000000001\"", body, StringComparison.Ordinal);
        Assert.Contains("le=\"0.25\"", body, StringComparison.Ordinal);

        // The §11 budget, as an exact boundary — so "how many pulses were over budget" is read off
        // the histogram rather than interpolated across a bucket that straddles the threshold.
        Assert.Contains("le=\"25\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Instrument_names_are_pinned_where_the_exporter_can_see_them()
    {
        // Views bind to an instrument by name. Spelled as constants so a rename breaks the build
        // rather than silently reverting the dashboard to default buckets — which still draws a
        // plausible line, which is the whole problem.
        Assert.Equal("muwbta.pulse.duration", EngineMetrics.PulseDurationInstrument);
        Assert.Equal("muwbta.command.latency", EngineMetrics.CommandLatencyInstrument);
        Assert.Equal("Muwbta.Engine", EngineMetrics.MeterName);
    }
}
