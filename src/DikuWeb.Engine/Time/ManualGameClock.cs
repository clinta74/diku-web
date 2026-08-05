namespace DikuWeb.Engine.Time;

/// <summary>
/// Test clock. Lives in the Engine rather than a test project so integration tests in
/// DikuWeb.Server.Tests can drive the loop deterministically too.
/// </summary>
public sealed class ManualGameClock(DateTimeOffset? startingAt = null) : IGameClock
{
    private DateTimeOffset _now = startingAt ?? DateTimeOffset.UnixEpoch;

    public long CurrentPulse { get; private set; }

    public DateTimeOffset UtcNow => _now;

    /// <summary>Advances the given number of pulses, moving wall time to match.</summary>
    public void AdvancePulses(long pulses = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pulses);
        CurrentPulse += pulses;
        _now += GameTiming.PulseInterval * pulses;
    }

    /// <summary>Advances by a duration, rounded down to whole pulses.</summary>
    public void Advance(TimeSpan duration) =>
        AdvancePulses((long)(duration.Ticks / GameTiming.PulseInterval.Ticks));
}
