namespace Muwbta.Engine.Time;

/// <summary>
/// PLAN.md §9: a game loop you cannot replay exactly is a game loop you cannot debug.
/// Every time-dependent decision in the Engine reads this rather than the system clock,
/// so tests can step pulses by hand and a "ten minutes of combat" test runs in
/// milliseconds without sleeping.
/// </summary>
public interface IGameClock
{
    /// <summary>Pulses elapsed since the loop started. Monotonic, never resets.</summary>
    long CurrentPulse { get; }

    /// <summary>Wall-clock time, for persistence timestamps rather than game logic.</summary>
    DateTimeOffset UtcNow { get; }
}
