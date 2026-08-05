using Microsoft.Extensions.Logging;

namespace DikuWeb.Engine;

/// <summary>
/// PLAN.md §2.4: source-generated log methods only. This matters most here - the loop runs
/// four times a second and allocation on it turns into GC pauses against the 25 ms budget.
/// </summary>
internal static partial class EngineLog
{
    [LoggerMessage(EventId = 2000, Level = LogLevel.Information,
        Message = "Game loop starting with {RoomCount} rooms, pulse {PulseMs} ms")]
    public static partial void LoopStarting(ILogger logger, int roomCount, double pulseMs);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information,
        Message = "Game loop stopped after {Pulses} pulses")]
    public static partial void LoopStopped(ILogger logger, long pulses);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Warning,
        Message = "Slow pulse {Pulse}: {ElapsedMs:F1} ms exceeds the {BudgetMs:F0} ms budget")]
    public static partial void SlowPulse(ILogger logger, long pulse, double elapsedMs, double budgetMs);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Error,
        Message = "Command '{Input}' from {Character} threw; the loop continues")]
    public static partial void CommandFailed(ILogger logger, string input, string character, Exception exception);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Error,
        Message = "Pulse {Pulse} threw outside command handling; the loop continues")]
    public static partial void PulseFailed(ILogger logger, long pulse, Exception exception);

    [LoggerMessage(EventId = 2005, Level = LogLevel.Information,
        Message = "{Character} entered the world at {Room}")]
    public static partial void PlayerEntered(ILogger logger, string character, string room);

    [LoggerMessage(EventId = 2006, Level = LogLevel.Information,
        Message = "{Character} left the world ({Reason})")]
    public static partial void PlayerLeft(ILogger logger, string character, string reason);

    [LoggerMessage(EventId = 2007, Level = LogLevel.Information,
        Message = "{Character} reconnected after being link-dead")]
    public static partial void PlayerReconnected(ILogger logger, string character);

    [LoggerMessage(EventId = 2008, Level = LogLevel.Warning,
        Message = "{Character} was saved in {Room}, which no longer exists; moved to {Fallback}")]
    public static partial void RelocatedFromMissingRoom(
        ILogger logger, string character, string room, string fallback);
}
