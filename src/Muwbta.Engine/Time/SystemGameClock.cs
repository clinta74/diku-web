namespace Muwbta.Engine.Time;

/// <summary>
/// Production clock. Pulse advancement is driven by the game loop calling
/// <see cref="Advance"/> once per tick, not by reading elapsed wall time - so a slow pulse
/// delays the next one rather than silently skipping game time.
/// </summary>
public sealed class SystemGameClock(TimeProvider timeProvider) : IGameClock
{
    private readonly TimeProvider _timeProvider =
        timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    private long _pulse;

    public long CurrentPulse => Interlocked.Read(ref _pulse);

    public DateTimeOffset UtcNow => _timeProvider.GetUtcNow();

    /// <summary>Called by the game loop at the end of each pulse.</summary>
    public long Advance() => Interlocked.Increment(ref _pulse);
}
