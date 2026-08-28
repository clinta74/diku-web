using DikuWeb.Playtest.Load;

namespace DikuWeb.Playtest.Tests;

/// <summary>
/// The arithmetic a load run's verdict rests on, tested against exposition the real exporter
/// produces rather than a tidied-up version of it.
/// </summary>
/// <remarks>
/// This is the half of load mode that can be tested without a server, and the half where a quiet
/// mistake would be invisible: a parser that finds no buckets reports an empty histogram, and an
/// empty histogram reports zero pulses over budget, which reads exactly like a healthy result.
/// </remarks>
public class MetricsSnapshotTests
{
    /// <summary>
    /// Shaped like the real thing, including the float formatting that makes the 0.1 boundary
    /// arrive as <c>0.10000000000000001</c> — see <see cref="PrometheusText"/> for why that
    /// detail is the one worth pinning.
    /// </summary>
    private const string Exposition = """
        # HELP dikuweb_pulse_duration_milliseconds Wall time for one game-loop pulse.
        # TYPE dikuweb_pulse_duration_milliseconds histogram
        dikuweb_pulse_duration_milliseconds_bucket{otel_scope_name="DikuWeb.Engine",le="0.10000000000000001"} 400
        dikuweb_pulse_duration_milliseconds_bucket{otel_scope_name="DikuWeb.Engine",le="0.25"} 700
        dikuweb_pulse_duration_milliseconds_bucket{otel_scope_name="DikuWeb.Engine",le="0.5"} 900
        dikuweb_pulse_duration_milliseconds_bucket{otel_scope_name="DikuWeb.Engine",le="1"} 950
        dikuweb_pulse_duration_milliseconds_bucket{otel_scope_name="DikuWeb.Engine",le="2"} 970
        dikuweb_pulse_duration_milliseconds_bucket{otel_scope_name="DikuWeb.Engine",le="5"} 980
        dikuweb_pulse_duration_milliseconds_bucket{otel_scope_name="DikuWeb.Engine",le="10"} 985
        dikuweb_pulse_duration_milliseconds_bucket{otel_scope_name="DikuWeb.Engine",le="25"} 990
        dikuweb_pulse_duration_milliseconds_bucket{otel_scope_name="DikuWeb.Engine",le="50"} 996
        dikuweb_pulse_duration_milliseconds_bucket{otel_scope_name="DikuWeb.Engine",le="100"} 999
        dikuweb_pulse_duration_milliseconds_bucket{otel_scope_name="DikuWeb.Engine",le="250"} 1000
        dikuweb_pulse_duration_milliseconds_bucket{otel_scope_name="DikuWeb.Engine",le="+Inf"} 1000
        dikuweb_pulse_duration_milliseconds_count{otel_scope_name="DikuWeb.Engine"} 1000
        dikuweb_pulse_duration_milliseconds_sum{otel_scope_name="DikuWeb.Engine"} 512.5
        # TYPE dikuweb_pulse_over_budget_total counter
        dikuweb_pulse_over_budget_total{otel_scope_name="DikuWeb.Engine"} 10
        # TYPE dikuweb_commands_handled_total counter
        dikuweb_commands_handled_total{otel_scope_name="DikuWeb.Engine"} 4200
        # TYPE dikuweb_sessions_active gauge
        dikuweb_sessions_active{otel_scope_name="DikuWeb.Engine"} 200
        # TYPE dikuweb_rooms_loaded gauge
        dikuweb_rooms_loaded{otel_scope_name="DikuWeb.Engine"} 143
        """;

    private static MetricsSnapshot Snapshot() =>
        MetricsSnapshot.Read(Exposition, DateTimeOffset.UnixEpoch);

    [Fact]
    public void The_pulse_histogram_survives_the_exporter_s_float_formatting()
    {
        var pulse = Snapshot().Pulse;

        Assert.Equal(1000, pulse.Count);

        // The boundary the exporter writes as 0.10000000000000001. Matched as a number, so the
        // bucket is found; matched as a string, this is 0 and every later number is nonsense.
        Assert.Equal(400, pulse.Buckets.Single(b => Math.Abs(b.UpperBound - 0.1) < 1e-9).CumulativeCount);
    }

    [Fact]
    public void Everything_over_the_budget_is_counted_rather_than_estimated()
    {
        var pulse = Snapshot().Pulse;

        // 990 of 1000 landed at or below 25 ms, so ten did not. Exact, because 25 is a boundary —
        // this is the number the §11 verdict is built on.
        Assert.Equal(10, pulse.CountAbove(25));
        Assert.Equal(0.01, pulse.ShareAbove(25));
    }

    [Fact]
    public void A_bound_that_is_not_a_bucket_edge_refuses_to_answer()
    {
        // Rather than interpolating a number that would look just as authoritative as the exact
        // one next to it. There is no le="30" bucket, so there is no honest count above 30.
        Assert.Null(Snapshot().Pulse.CountAbove(30));
        Assert.Null(Snapshot().Pulse.ShareAbove(30));
    }

    [Fact]
    public void A_quantile_carries_the_bucket_it_was_interpolated_inside()
    {
        var p99 = Snapshot().Pulse.At(0.99);

        Assert.NotNull(p99);

        // The 990th observation is the last one in the 10–25 bucket, so p99 is somewhere in there
        // and the estimate has to say so rather than presenting three decimals as measurement.
        Assert.Equal(10, p99.BucketLower);
        Assert.Equal(25, p99.BucketUpper);
        Assert.InRange(p99.Estimate, 10, 25);
    }

    [Fact]
    public void Counters_and_gauges_are_read_through_the_suffix_the_exporter_adds()
    {
        var snapshot = Snapshot();

        // The instrument is dikuweb.pulse.over_budget; the exporter renames it with _total.
        Assert.Equal(10, snapshot.PulsesOverBudget);
        Assert.Equal(4200, snapshot.CommandsHandled);

        // Gauges get no suffix, so both spellings have to work.
        Assert.Equal(200, snapshot.SessionsActive);
        Assert.Equal(143, snapshot.RoomsLoaded);
    }

    [Fact]
    public void The_window_is_the_difference_between_two_scrapes()
    {
        // Every instrument on /metrics is a total since boot. A run that reported the later scrape
        // as-is would be averaging in however long the server sat idle before anybody logged in —
        // which flatters a steady-state number by exactly the amount nobody was playing.
        var before = MetricsSnapshot.Read(Exposition, DateTimeOffset.UnixEpoch).Pulse;

        var after = MetricsSnapshot.Read(
            Exposition
                .Replace("le=\"25\"} 990", "le=\"25\"} 1980", StringComparison.Ordinal)
                .Replace("le=\"50\"} 996", "le=\"50\"} 1992", StringComparison.Ordinal)
                .Replace("le=\"100\"} 999", "le=\"100\"} 1998", StringComparison.Ordinal)
                .Replace("le=\"250\"} 1000", "le=\"250\"} 2000", StringComparison.Ordinal)
                .Replace("le=\"+Inf\"} 1000", "le=\"+Inf\"} 2000", StringComparison.Ordinal)
                .Replace("_count{otel_scope_name=\"DikuWeb.Engine\"} 1000", "_count{otel_scope_name=\"DikuWeb.Engine\"} 2000", StringComparison.Ordinal),
            DateTimeOffset.UnixEpoch.AddMinutes(2)).Pulse;

        var window = after.Since(before);

        Assert.Equal(1000, window.Count);

        // Ten were over budget before, twenty in total, so ten happened during the window.
        Assert.Equal(10, window.CountAbove(25));
    }

    [Fact]
    public void An_exposition_with_no_pulse_samples_is_an_empty_histogram_not_a_crash()
    {
        // The apparatus turns this into a refusal to report rather than a zero — see MetricsProbe.
        // What must not happen is that it parses cleanly into "no pulses were over budget".
        var snapshot = MetricsSnapshot.Read("# nothing here\n", DateTimeOffset.UnixEpoch);

        Assert.Equal(0, snapshot.Pulse.Count);
        Assert.Null(snapshot.Pulse.ShareAbove(25));
    }

    [Fact]
    public void Labels_containing_a_comma_do_not_split_into_two()
    {
        var samples = PrometheusText.Parse(
            "thing{a=\"one, two\",b=\"three\"} 7\n");

        var sample = Assert.Single(samples);

        Assert.Equal("one, two", sample.Labels["a"]);
        Assert.Equal("three", sample.Labels["b"]);
        Assert.Equal(7, sample.Value);
    }
}
