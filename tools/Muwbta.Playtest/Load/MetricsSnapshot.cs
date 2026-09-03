using System.Globalization;

namespace Muwbta.Playtest.Load;

/// <summary>
/// A histogram as the exposition gives it: cumulative counts against upper bounds.
/// </summary>
/// <remarks>
/// Buckets are <em>cumulative</em> — <c>le="25"</c> counts everything at or below 25 ms, not the
/// slice between 10 and 25 — and every count is a monotonic total since the process started. Both
/// facts are why <see cref="Since"/> exists: the answer to "what did the loop do while 200 people
/// were playing" is the difference between two scrapes, not either one of them.
/// </remarks>
public sealed record Histogram(IReadOnlyList<HistogramBucket> Buckets, long Count, double Sum)
{
    public static readonly Histogram Empty = new([], 0, 0);

    /// <summary>The mean observation, in whatever unit the instrument records.</summary>
    public double Mean => Count == 0 ? 0 : Sum / Count;

    /// <summary>
    /// How many observations exceeded <paramref name="bound"/>, <b>exactly</b>, when it is one of
    /// the bucket boundaries.
    /// </summary>
    /// <remarks>
    /// This is the number worth reporting and the reason the exporter puts an explicit boundary at
    /// 25: the §11 budget is a bucket edge, so "how many pulses were over budget" is read off the
    /// histogram rather than estimated across a bucket. Returns null when the bound is not a
    /// boundary, rather than quietly interpolating an answer that would look just as authoritative.
    /// </remarks>
    public long? CountAbove(double bound)
    {
        var bucket = Buckets.FirstOrDefault(b => Math.Abs(b.UpperBound - bound) < 1e-9);
        return bucket is null ? null : Count - bucket.CumulativeCount;
    }

    /// <inheritdoc cref="CountAbove"/>
    public double? ShareAbove(double bound) =>
        Count == 0 ? null : CountAbove(bound) is { } above ? (double)above / Count : null;

    /// <summary>
    /// The value at <paramref name="quantile"/>, interpolated inside whichever bucket it lands in.
    /// </summary>
    /// <remarks>
    /// <b>An estimate, and only ever as good as the buckets.</b> Everything inside one bucket is
    /// indistinguishable, so a p99 that lands in the 0.25–0.5 bucket is really "somewhere in
    /// 0.25–0.5" and the decimals are arithmetic rather than measurement. Reported with its
    /// bucket alongside for that reason — see <see cref="Quantile"/>'s <c>Bucket</c>.
    /// </remarks>
    public QuantileEstimate? At(double quantile)
    {
        if (Count == 0 || Buckets.Count == 0)
        {
            return null;
        }

        var wanted = quantile * Count;
        var lowerBound = 0d;
        var lowerCount = 0L;

        foreach (var bucket in Buckets)
        {
            if (bucket.CumulativeCount >= wanted)
            {
                if (double.IsPositiveInfinity(bucket.UpperBound))
                {
                    // Everything past the last finite boundary is unbounded above: there is no
                    // interpolation to do and pretending otherwise would invent a number.
                    return new QuantileEstimate(lowerBound, double.PositiveInfinity, lowerBound);
                }

                var inBucket = bucket.CumulativeCount - lowerCount;

                var estimate = inBucket <= 0
                    ? bucket.UpperBound
                    : lowerBound + (((wanted - lowerCount) / inBucket) * (bucket.UpperBound - lowerBound));

                return new QuantileEstimate(lowerBound, bucket.UpperBound, estimate);
            }

            lowerBound = bucket.UpperBound;
            lowerCount = bucket.CumulativeCount;
        }

        return null;
    }

    /// <summary>
    /// What happened between an earlier scrape and this one.
    /// </summary>
    /// <remarks>
    /// The whole measurement rests on this. Scraped totals include every pulse since boot — the
    /// idle minutes before anybody logged in, the ramp while sessions were still arriving — and
    /// averaging those in would flatter a steady-state number by however long the server had been
    /// sitting quiet. Subtracting gives exactly the window that was asked about.
    /// </remarks>
    public Histogram Since(Histogram earlier)
    {
        ArgumentNullException.ThrowIfNull(earlier);

        var before = earlier.Buckets.ToDictionary(b => b.UpperBound, b => b.CumulativeCount);

        var buckets = Buckets
            .Select(b => b with
            {
                CumulativeCount = b.CumulativeCount - before.GetValueOrDefault(b.UpperBound, 0L),
            })
            .ToList();

        return new Histogram(buckets, Count - earlier.Count, Sum - earlier.Sum);
    }
}

/// <summary>One cumulative bucket: everything at or below <paramref name="UpperBound"/>.</summary>
public sealed record HistogramBucket(double UpperBound, long CumulativeCount);

/// <summary>
/// An interpolated quantile, carried with the bucket it was interpolated inside.
/// </summary>
/// <remarks>
/// The bucket travels with the number so a reader can see how much of it is measurement. A p99 of
/// "0.31 ms (bucket 0.25–0.5)" is honest in a way that a bare "0.31 ms" is not.
/// </remarks>
public sealed record QuantileEstimate(double BucketLower, double BucketUpper, double Estimate)
{
    public override string ToString() =>
        double.IsPositiveInfinity(BucketUpper)
            ? $"over {Format(BucketLower)} (top bucket — unbounded)"
            : $"{Format(Estimate)} (bucket {Format(BucketLower)}–{Format(BucketUpper)})";

    private static string Format(double value) =>
        value.ToString(value < 10 ? "0.###" : "0.#", CultureInfo.InvariantCulture);
}

/// <summary>
/// Everything one scrape of <c>/metrics</c> said that a load run cares about.
/// </summary>
public sealed record MetricsSnapshot(
    DateTimeOffset At,
    Histogram Pulse,
    Histogram CommandLatency,
    long CommandsHandled,
    long PulsesOverBudget,
    int SessionsActive,
    int RoomsLoaded)
{
    /// <summary>The instrument names, as the Prometheus exporter renames them.</summary>
    /// <remarks>
    /// OpenTelemetry appends the unit and, for a counter, <c>_total</c>: the instrument
    /// <c>muwbta.pulse.duration</c> recorded in <c>ms</c> is exported as
    /// <c>muwbta_pulse_duration_milliseconds</c>. Spelled out here because the apparatus is a
    /// client and cannot reference <c>EngineMetrics</c> to ask.
    /// </remarks>
    public const string PulseFamily = "muwbta_pulse_duration_milliseconds";

    /// <inheritdoc cref="PulseFamily"/>
    public const string CommandLatencyFamily = "muwbta_command_latency_milliseconds";

    public static MetricsSnapshot Read(string exposition, DateTimeOffset at)
    {
        var samples = PrometheusText.Parse(exposition);

        return new MetricsSnapshot(
            at,
            ReadHistogram(samples, PulseFamily),
            ReadHistogram(samples, CommandLatencyFamily),
            (long)Counter(samples, "muwbta_commands_handled"),
            (long)Counter(samples, "muwbta_pulse_over_budget"),
            (int)Counter(samples, "muwbta_sessions_active"),
            (int)Counter(samples, "muwbta_rooms_loaded"));
    }

    private static Histogram ReadHistogram(IReadOnlyList<PrometheusSample> samples, string family)
    {
        var buckets = samples
            .Where(s => s.Name == family + "_bucket" && s.Labels.ContainsKey("le"))
            .Select(s => new HistogramBucket(Bound(s.Labels["le"]), (long)s.Value))
            .OrderBy(b => b.UpperBound)
            .ToList();

        if (buckets.Count == 0)
        {
            return Histogram.Empty;
        }

        var count = (long)Single(samples, family + "_count");
        var sum = Single(samples, family + "_sum");

        return new Histogram(buckets, count, sum);
    }

    private static double Bound(string le) =>
        le is "+Inf" or "inf"
            ? double.PositiveInfinity
            : double.Parse(le, NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>
    /// A counter or gauge by name, tolerating the <c>_total</c> suffix the exporter adds.
    /// </summary>
    private static double Counter(IReadOnlyList<PrometheusSample> samples, string name) =>
        Single(samples, name + "_total") is var total and not 0 ? total : Single(samples, name);

    private static double Single(IReadOnlyList<PrometheusSample> samples, string name) =>
        samples.Where(s => s.Name == name).Sum(s => s.Value);
}
