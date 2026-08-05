namespace DikuWeb.Engine.Time;

/// <summary>
/// PLAN.md §2.3. One pulse is 250 ms; every system runs on a pulse multiple.
/// </summary>
public static class GameTiming
{
    public static readonly TimeSpan PulseInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>PLAN.md §11: p99 pulse duration under 25 ms, i.e. 10% of the budget.</summary>
    public static readonly TimeSpan PulseBudget = TimeSpan.FromMilliseconds(25);

    public const int CommandDrainPulses = 1;
    public const int CombatRoundPulses = 8;      // 2 s
    public const int MobAiPulses = 16;           // 4 s
    public const int SpawnSweepPulses = 60;      // 15 s
    public const int RegenPulses = 240;          // 60 s
    public const int AutosavePulses = 1200;      // 5 min

    /// <summary>True when a system with the given cadence should run on this pulse.</summary>
    public static bool RunsOn(long pulse, int everyPulses)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(everyPulses);
        return pulse % everyPulses == 0;
    }
}
