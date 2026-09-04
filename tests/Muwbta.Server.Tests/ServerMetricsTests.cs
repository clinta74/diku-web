using System.Diagnostics.Metrics;
using Muwbta.Server.Telemetry;

namespace Muwbta.Server.Tests;

/// <summary>
/// The security counters record what they say they do, with the tag the dashboard slices on.
/// </summary>
public sealed class ServerMetricsTests
{
    private sealed class Recorder : IDisposable
    {
        private readonly MeterListener _listener = new();

        public List<(string Instrument, long Value, string? Tag)> Measurements { get; } = [];

        public Recorder()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == ServerMetrics.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };

            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            {
                string? tag = null;
                foreach (var pair in tags)
                {
                    tag = pair.Value?.ToString();
                }

                Measurements.Add((instrument.Name, value, tag));
            });

            _listener.Start();
        }

        public long Total(string instrument, string? tag = null) =>
            Measurements
                .Where(m => m.Instrument == instrument && (tag is null || m.Tag == tag))
                .Sum(m => m.Value);

        public void Dispose() => _listener.Dispose();
    }

    [Fact]
    public void Sign_ins_are_counted_by_outcome()
    {
        using var recorder = new Recorder();
        using var metrics = new ServerMetrics();

        metrics.SignIn(SignInOutcome.Success);
        metrics.SignIn(SignInOutcome.WrongPassword);
        metrics.SignIn(SignInOutcome.WrongPassword);
        metrics.SignIn(SignInOutcome.Paused);

        Assert.Equal(1, recorder.Total("muwbta.signins", "success"));
        Assert.Equal(2, recorder.Total("muwbta.signins", "wrong_password"));
        Assert.Equal(1, recorder.Total("muwbta.signins", "paused"));
        Assert.Equal(4, recorder.Total("muwbta.signins"));
    }

    [Fact]
    public void A_pause_is_its_own_event()
    {
        using var recorder = new Recorder();
        using var metrics = new ServerMetrics();

        metrics.SignInPaused();

        Assert.Equal(1, recorder.Total("muwbta.signin.pauses"));
        Assert.Equal(0, recorder.Total("muwbta.signins"));
    }

    [Theory]
    [InlineData("auth")]
    [InlineData("commands")]
    public void Rate_limit_rejections_carry_the_policy(string policy)
    {
        using var recorder = new Recorder();
        using var metrics = new ServerMetrics();

        metrics.RateLimited(policy);

        Assert.Equal(1, recorder.Total("muwbta.ratelimit.rejections", policy));
    }

    [Fact]
    public void Registrations_moderation_and_save_failures_are_counted()
    {
        using var recorder = new Recorder();
        using var metrics = new ServerMetrics();

        metrics.Registration("created");
        metrics.Registration("refused");
        metrics.Moderation("Banned");
        metrics.SaveFailed(characters: 12);

        Assert.Equal(1, recorder.Total("muwbta.registrations", "created"));
        Assert.Equal(1, recorder.Total("muwbta.registrations", "refused"));
        Assert.Equal(1, recorder.Total("muwbta.moderation.actions", "Banned"));

        // One failed batch is one failure, whatever its size: the question is how often, not how big.
        Assert.Equal(1, recorder.Total("muwbta.saves.failed"));
    }

    [Theory]
    [InlineData("muwbta.signins")]
    [InlineData("muwbta.signin.pauses")]
    [InlineData("muwbta.registrations")]
    [InlineData("muwbta.ratelimit.rejections")]
    [InlineData("muwbta.moderation.actions")]
    [InlineData("muwbta.saves.failed")]
    public void Every_instrument_is_published_under_the_server_meter(string name)
    {
        // The exporter subscribes by meter name; an instrument on any other meter is one the
        // dashboard never sees, and the panel it feeds stays quietly empty.
        var published = new List<string>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, _) =>
            {
                if (instrument.Meter.Name == ServerMetrics.MeterName)
                {
                    published.Add(instrument.Name);
                }
            },
        };
        listener.Start();

        using var metrics = new ServerMetrics();

        Assert.Contains(name, published);
    }
}
