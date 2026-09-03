using System.Diagnostics.Metrics;

namespace Muwbta.Engine.Telemetry;

/// <summary>
/// What the running world measures about itself (PLAN.md §8, Phase 6, against the §11 targets).
/// </summary>
/// <remarks>
/// <b>Instruments only, no exporter.</b> These are <see cref="System.Diagnostics.Metrics"/>
/// primitives from the base library, so nothing is added to the dependency graph and nothing
/// leaves the process on its own. A deployment that wants them shipped adds an OpenTelemetry
/// exporter pointed at <see cref="MeterName"/> and changes no code here — which is the whole
/// reason to draw the line in this place. Instrumentation has to live in the code; where it is
/// sent is a deployment decision, and one that can be made later.
///
/// <b>Why this exists.</b> §11 states four targets and, until now, nothing measured any of them.
/// The slow-pulse watchdog logs a line when a pulse runs over budget, which answers "did that
/// happen" and cannot answer "is it getting worse" — a log has no distribution, so one bad pulse
/// and a p99 that has been degrading for a week look identical.
///
/// <b>Cheap enough to leave on.</b> A histogram record with no listener attached is a handful of
/// nanoseconds; the loop already calls <see cref="System.Diagnostics.Stopwatch"/> around every
/// pulse for the watchdog, so the timing itself costs nothing new. Nothing here allocates on the
/// hot path.
/// </remarks>
public sealed class EngineMetrics : IDisposable
{
    /// <summary>The name an exporter subscribes to. Stable: renaming it breaks every dashboard.</summary>
    public const string MeterName = "Muwbta.Engine";

    /// <summary>
    /// The two instrument names an exporter has to configure by name, rather than string literals
    /// repeated at the far end.
    /// </summary>
    /// <remarks>
    /// Both histograms need explicit bucket boundaries to be worth reading — the default set is
    /// far too coarse for a loop whose healthy pulse is under a millisecond — and a view is bound
    /// to an instrument <em>by name</em>. Spelled here so a rename breaks the build instead of
    /// silently reverting the dashboard to default buckets, which still draws a plausible line.
    /// </remarks>
    public const string PulseDurationInstrument = "muwbta.pulse.duration";

    /// <inheritdoc cref="PulseDurationInstrument"/>
    public const string CommandLatencyInstrument = "muwbta.command.latency";

    private readonly Meter _meter;
    private readonly Histogram<double> _pulseDuration;
    private readonly Histogram<double> _commandLatency;
    private readonly Counter<long> _commands;
    private readonly Counter<long> _slowPulses;

    public EngineMetrics(IMeterFactory? factory = null)
    {
        _meter = factory?.Create(MeterName) ?? new Meter(MeterName);

        // Milliseconds, and a double, because the interesting values are fractions of one: a
        // healthy pulse is well under a millisecond and the budget is 25 (§11). Integer
        // milliseconds would report almost every pulse as zero.
        _pulseDuration = _meter.CreateHistogram<double>(
            PulseDurationInstrument,
            unit: "ms",
            description: "Wall time for one game-loop pulse. §11 targets p99 under 25 ms.");

        // The half of "command to first SSE byte" that this process controls: from the moment a
        // command was accepted at the gateway to the moment the loop finished handling it. The
        // rest is network, which is not ours to measure from in here.
        _commandLatency = _meter.CreateHistogram<double>(
            CommandLatencyInstrument,
            unit: "ms",
            description: "Gateway acceptance to handler completion. The in-process part of the §11 command target.");

        _commands = _meter.CreateCounter<long>(
            "muwbta.commands.handled",
            description: "Player commands executed by the loop.");

        // Counted as well as logged. The log says which pulses were slow; the counter says how
        // often, which is the question you ask when deciding whether it matters.
        _slowPulses = _meter.CreateCounter<long>(
            "muwbta.pulse.over_budget",
            description: "Pulses that exceeded the §11 budget.");
    }

    /// <summary>
    /// Publishes the gauges that read live world state.
    /// </summary>
    /// <remarks>
    /// Observable rather than pushed, so nothing has to remember to update them when a player
    /// joins or leaves — the value is read at collection time from the thing that already knows.
    /// The callbacks run on the collecting thread, which is why they only read counts: anything
    /// that walked the world here would be touching loop-owned state from outside the loop.
    /// </remarks>
    public void PublishGauges(Func<int> playerCount, Func<int> roomCount)
    {
        ArgumentNullException.ThrowIfNull(playerCount);
        ArgumentNullException.ThrowIfNull(roomCount);

        _meter.CreateObservableGauge(
            "muwbta.sessions.active",
            playerCount,
            description: "Characters currently in the world. §11 targets 200 on one process.");

        _meter.CreateObservableGauge(
            "muwbta.rooms.loaded",
            roomCount,
            description: "Rooms held in memory.");
    }

    public void RecordPulse(double milliseconds, bool overBudget)
    {
        _pulseDuration.Record(milliseconds);

        if (overBudget)
        {
            _slowPulses.Add(1);
        }
    }

    /// <summary>
    /// Records one handled command and how long it waited plus ran.
    /// </summary>
    /// <param name="milliseconds">
    /// Null when the message carried no accepted-at timestamp, which is every message the Server
    /// submits on its own behalf rather than on a player's. Counted, but not timed — averaging a
    /// builder's world reload into the command latency would make the number mean nothing.
    /// </param>
    public void RecordCommand(double? milliseconds)
    {
        _commands.Add(1);

        if (milliseconds is { } elapsed)
        {
            _commandLatency.Record(elapsed);
        }
    }

    public void Dispose() => _meter.Dispose();
}
