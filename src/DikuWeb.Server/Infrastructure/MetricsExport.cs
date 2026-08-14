using DikuWeb.Engine.Telemetry;
using OpenTelemetry.Metrics;

namespace DikuWeb.Server.Infrastructure;

/// <summary>
/// Where <see cref="EngineMetrics"/> goes (PLAN.md §8, Phase 6).
/// </summary>
/// <remarks>
/// <b>This is the deployment half of a line drawn deliberately.</b> <see cref="EngineMetrics"/>
/// takes no dependency on OpenTelemetry and never will: it records to
/// <c>System.Diagnostics.Metrics</c> primitives from the base library, and a histogram with no
/// listener attached costs a handful of nanoseconds. Instrumentation belongs in the code; where it
/// is shipped is a deployment decision. All of that decision lives in this file.
///
/// <b>Pull, not push.</b> Prometheus scrapes <c>/metrics</c> rather than the process pushing to a
/// collector. That is one fewer container, and it buys the <c>up</c> series for free — the one that
/// says the server stopped answering at all, which is the alert you actually want and which a push
/// pipeline cannot distinguish from a quiet night.
///
/// <b><c>/metrics</c> is unauthenticated, and must stay unreachable from outside.</b> nginx
/// forwards only <c>/api/</c> and <c>/health</c> (<c>client/nginx.conf.template</c>), so the
/// endpoint answers on the compose network and nowhere else. Anyone adding a <c>location /</c>
/// passthrough to that config is publishing it; there is nothing secret in the numbers, but room
/// counts and session counts are more than a stranger should get for free.
/// </remarks>
public static class MetricsExport
{
    public static IServiceCollection AddDikuWebMetricsExport(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services
            .AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddMeter(EngineMetrics.MeterName)
                // The loop is one thread that must not be starved, so GC pauses and thread-pool
                // starvation are engine problems here in a way they are not in a request/response
                // service: a blocked pulse is the whole world stopping, not one slow response.
                .AddRuntimeInstrumentation()
                .AddAspNetCoreInstrumentation()
                .AddView(
                    EngineMetrics.PulseDurationInstrument,
                    PulseBuckets)
                .AddView(
                    EngineMetrics.CommandLatencyInstrument,
                    CommandBuckets)
                .AddPrometheusExporter());

        return services;
    }

    /// <summary>
    /// Buckets for pulse duration, in milliseconds.
    /// </summary>
    /// <remarks>
    /// <b>The default buckets would make this instrument useless</b>, which is worth stating
    /// plainly because the dashboard would still draw a line. OpenTelemetry's defaults start
    /// 0, 5, 10, 25, 50 — and a healthy pulse here is well under a single millisecond, so
    /// essentially every observation lands in the first bucket and every quantile the dashboard
    /// computes is an interpolation inside 0–5 ms. The p99 panel would be a straight line at
    /// roughly 4.95 ms forever, and it would look like a measurement.
    ///
    /// So: fine resolution below a millisecond, where the distribution actually lives, and an
    /// exact boundary at 25 — the §11 budget. A boundary on the threshold means "how many pulses
    /// were over budget" is read off the histogram rather than estimated across one.
    /// </remarks>
    private static readonly ExplicitBucketHistogramConfiguration PulseBuckets = new()
    {
        Boundaries = [0.1, 0.25, 0.5, 1, 2, 5, 10, 25, 50, 100, 250],
    };

    /// <summary>
    /// Buckets for command latency, in milliseconds. Wider than <see cref="PulseBuckets"/>: this
    /// one includes time queued waiting for the loop, so its interesting range is tens of
    /// milliseconds rather than fractions of one.
    /// </summary>
    private static readonly ExplicitBucketHistogramConfiguration CommandBuckets = new()
    {
        Boundaries = [1, 2, 5, 10, 25, 50, 100, 250, 500, 1000, 2500],
    };
}
