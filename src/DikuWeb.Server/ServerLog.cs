namespace DikuWeb.Server;

/// <summary>
/// PLAN.md §2.4: all log messages are declared as [LoggerMessage] source-generated methods,
/// never inline template strings.
/// </summary>
/// <remarks>
/// This is a performance requirement rather than a style rule. A conventional
/// _logger.LogDebug("...{A}...", a) allocates a params object[] and boxes value types at the
/// call site, before the provider decides the level is disabled. On the game loop at 4 pulses
/// per second across 200 sessions that is constant allocation pressure, and the resulting GC
/// pauses land directly on the 25 ms p99 pulse budget (PLAN.md §11). The generator emits an
/// IsEnabled check first and returns before doing any work.
/// </remarks>
internal static partial class ServerLog
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "DikuWeb server starting in {Environment}")]
    public static partial void Starting(ILogger logger, string environment);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Database connection configured for host {Host}, database {Database}")]
    public static partial void DatabaseConfigured(ILogger logger, string host, string database);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Warning,
        Message = "Readiness check failed: {Check} reported {Status}")]
    public static partial void ReadinessCheckFailed(ILogger logger, string check, string status);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Information,
        Message = "Applying database migrations (development only)")]
    public static partial void ApplyingMigrations(ILogger logger);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Information,
        Message = "Seeded the starter world; players begin at {StartingRoom}")]
    public static partial void SeededStarterWorld(ILogger logger, string startingRoom);

    [LoggerMessage(
        EventId = 1005,
        Level = LogLevel.Information,
        Message = "World already present, skipping seed")]
    public static partial void SeedSkipped(ILogger logger);

    [LoggerMessage(
        EventId = 1006,
        Level = LogLevel.Information,
        Message = "SSE stream closed for {Character}")]
    public static partial void StreamClosed(ILogger logger, string character);

    [LoggerMessage(
        EventId = 1007,
        Level = LogLevel.Error,
        Message = "Failed to save a batch of {Count} character(s); the worker continues")]
    public static partial void CharacterSaveFailed(ILogger logger, int count, Exception exception);
}
