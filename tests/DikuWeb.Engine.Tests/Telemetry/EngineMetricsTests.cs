using System.Diagnostics.Metrics;
using DikuWeb.Engine.Telemetry;

namespace DikuWeb.Engine.Tests.Telemetry;

/// <summary>
/// The instruments the running world publishes (PLAN.md §8, Phase 6, against the §11 targets).
/// </summary>
/// <remarks>
/// §11 states four performance targets and, until Phase 6, nothing measured any of them — the
/// slow-pulse watchdog logged a line when a pulse ran over budget, which answers "did that happen"
/// and cannot answer "is it getting worse". These assert the instruments exist, are named what a
/// dashboard will be built against, and record what they claim to.
///
/// <b>Deliberately not asserted here: the numbers themselves.</b> A p99 measured on a build agent
/// under an xUnit host is not the p99 §11 is about, and a test that pinned one would fail for
/// reasons that have nothing to do with the game. What is worth pinning is that the measurement
/// exists and is correct — the numbers come from a real deployment reading these.
/// </remarks>
public sealed class EngineMetricsTests
{
    /// <summary>Collects everything one meter emits, so a test can assert on it.</summary>
    private sealed class Recorder : IDisposable
    {
        private readonly MeterListener _listener = new();

        public List<(string Instrument, double Value)> Measurements { get; } = [];

        public Recorder(string meterName)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == meterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };

            _listener.SetMeasurementEventCallback<double>((instrument, value, _, _) =>
                Measurements.Add((instrument.Name, value)));

            _listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
                Measurements.Add((instrument.Name, value)));

            _listener.SetMeasurementEventCallback<int>((instrument, value, _, _) =>
                Measurements.Add((instrument.Name, value)));

            _listener.Start();
        }

        public void Collect() => _listener.RecordObservableInstruments();

        public IEnumerable<double> ValuesOf(string instrument) =>
            Measurements.Where(m => m.Instrument == instrument).Select(m => m.Value);

        public void Dispose() => _listener.Dispose();
    }

    /// <summary>
    /// A meter per test, through a factory, so two tests listening at once cannot see each
    /// other's measurements.
    /// </summary>
    private static (EngineMetrics Metrics, Recorder Recorder) NewMetrics()
    {
        // Distinct name per test, since MeterListener matches on it and the real name is a
        // process-wide constant.
        var name = $"{EngineMetrics.MeterName}.{Guid.NewGuid():N}";
        var recorder = new Recorder(name);

        return (new EngineMetrics(new TestMeterFactory(name)), recorder);
    }

    private sealed class TestMeterFactory(string name) : IMeterFactory
    {
        private readonly List<Meter> _meters = [];

        public Meter Create(MeterOptions options)
        {
            var meter = new Meter(name);
            _meters.Add(meter);
            return meter;
        }

        public void Dispose()
        {
            foreach (var meter in _meters)
            {
                meter.Dispose();
            }
        }
    }

    [Fact]
    public void Every_pulse_is_recorded_not_only_the_slow_ones()
    {
        // The whole reason this is a histogram rather than a log line. A distribution needs the
        // healthy pulses too, or the p99 it reports is the p99 of the failures.
        var (metrics, recorder) = NewMetrics();
        using var _ = recorder;
        using var __ = metrics;

        metrics.RecordPulse(0.4, overBudget: false);
        metrics.RecordPulse(0.6, overBudget: false);
        metrics.RecordPulse(40, overBudget: true);

        Assert.Equal([0.4, 0.6, 40], recorder.ValuesOf("dikuweb.pulse.duration"));
    }

    [Fact]
    public void A_pulse_over_budget_is_counted_as_well_as_timed()
    {
        // The log says which pulses were slow; the counter says how often, which is the question
        // you ask when deciding whether it matters.
        var (metrics, recorder) = NewMetrics();
        using var _ = recorder;
        using var __ = metrics;

        metrics.RecordPulse(1, overBudget: false);
        metrics.RecordPulse(40, overBudget: true);
        metrics.RecordPulse(90, overBudget: true);

        Assert.Equal(2, recorder.ValuesOf("dikuweb.pulse.over_budget").Sum());
    }

    [Fact]
    public void A_command_is_counted_and_timed()
    {
        var (metrics, recorder) = NewMetrics();
        using var _ = recorder;
        using var __ = metrics;

        metrics.RecordCommand(12.5);

        Assert.Equal(1, recorder.ValuesOf("dikuweb.commands.handled").Sum());
        Assert.Equal([12.5], recorder.ValuesOf("dikuweb.command.latency"));
    }

    [Fact]
    public void A_command_with_no_timestamp_is_counted_but_not_timed()
    {
        // Messages the Server submits on its own behalf carry no acceptance time. Averaging a
        // builder's world reload into the command latency would make the number mean nothing.
        var (metrics, recorder) = NewMetrics();
        using var _ = recorder;
        using var __ = metrics;

        metrics.RecordCommand(null);

        Assert.Equal(1, recorder.ValuesOf("dikuweb.commands.handled").Sum());
        Assert.Empty(recorder.ValuesOf("dikuweb.command.latency"));
    }

    [Fact]
    public void The_gauges_read_live_state_at_collection_time()
    {
        // Observable rather than pushed, so nothing has to remember to update them when a player
        // joins or leaves.
        var (metrics, recorder) = NewMetrics();
        using var _ = recorder;
        using var __ = metrics;

        var players = 0;
        metrics.PublishGauges(() => players, () => 42);

        players = 7;
        recorder.Collect();

        Assert.Equal([7], recorder.ValuesOf("dikuweb.sessions.active"));
        Assert.Equal([42], recorder.ValuesOf("dikuweb.rooms.loaded"));
    }

    [Fact]
    public void The_meter_is_named_what_a_dashboard_will_be_built_against()
    {
        // Renaming this breaks every dashboard and every exporter configuration pointed at it,
        // silently — the metrics simply stop arriving. Pinned so the rename has to be deliberate.
        Assert.Equal("DikuWeb.Engine", EngineMetrics.MeterName);
    }

    [Theory]
    [InlineData("dikuweb.pulse.duration")]
    [InlineData("dikuweb.pulse.over_budget")]
    [InlineData("dikuweb.command.latency")]
    [InlineData("dikuweb.commands.handled")]
    [InlineData("dikuweb.sessions.active")]
    [InlineData("dikuweb.rooms.loaded")]
    public void The_instrument_names_are_pinned(string name)
    {
        // Same reasoning as the meter name, one level down.
        var (metrics, recorder) = NewMetrics();
        using var _ = recorder;
        using var __ = metrics;

        metrics.PublishGauges(() => 1, () => 1);
        metrics.RecordPulse(1, overBudget: true);
        metrics.RecordCommand(1);
        recorder.Collect();

        Assert.Contains(name, recorder.Measurements.Select(m => m.Instrument));
    }
}
