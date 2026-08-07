using DikuWeb.Domain.Worlds;
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

    [LoggerMessage(EventId = 2009, Level = LogLevel.Information,
        Message = "Applied {Kind} '{Key}' as {Count} write(s)")]
    public static partial void MutationApplied(ILogger logger, string kind, string key, int count);

    [LoggerMessage(EventId = 2010, Level = LogLevel.Information,
        Message = "Refused {Kind} '{Key}': {Reason}")]
    public static partial void MutationRefused(ILogger logger, string kind, string key, string reason);

    [LoggerMessage(EventId = 2011, Level = LogLevel.Error,
        Message = "Applying {Kind} '{Key}' threw; the loop continues")]
    public static partial void MutationFailed(ILogger logger, string kind, string key, Exception exception);

    [LoggerMessage(EventId = 2012, Level = LogLevel.Warning,
        Message = "World reloaded from the database: {RoomCount} rooms")]
    public static partial void WorldReloaded(ILogger logger, int roomCount);

    [LoggerMessage(EventId = 2013, Level = LogLevel.Error,
        Message = "World reload failed; memory may be ahead of the database")]
    public static partial void WorldReloadFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2014, Level = LogLevel.Warning,
        Message = "Exit {Room} {Direction} points at '{Target}', which does not exist")]
    public static partial void DanglingExit(
        ILogger logger, string room, string direction, string target);

    [LoggerMessage(EventId = 2015, Level = LogLevel.Information,
        Message = "{Character} is now {Role} in the world")]
    public static partial void ActorRoleChanged(ILogger logger, string character, string role);

    [LoggerMessage(EventId = 2016, Level = LogLevel.Warning,
        Message = "Spawner {SpawnerId} template '{TemplateKey}' not found")]
    public static partial void SpawnerTemplateNotFound(ILogger logger, Guid spawnerId, string templateKey);

    [LoggerMessage(EventId = 2017, Level = LogLevel.Information,
        Message = "PvP kill: {Killer} killed {Victim} in {Room}")]
    public static partial void PvpKill(ILogger logger, string killer, string victim, string room);

    [LoggerMessage(EventId = 2018, Level = LogLevel.Information,
        Message = "{Character} cast {Ability}")]
    public static partial void AbilityCast(ILogger logger, string character, string ability);

    [LoggerMessage(EventId = 2019, Level = LogLevel.Information,
        Message = "{Character}'s {Ability} resolves")]
    public static partial void AbilityResolving(ILogger logger, string character, string ability);

    [LoggerMessage(EventId = 2020, Level = LogLevel.Information,
        Message = "{Character}'s {Ability} was interrupted")]
    public static partial void AbilityCastInterrupted(ILogger logger, string character, string ability);

    [LoggerMessage(EventId = 2021, Level = LogLevel.Debug,
        Message = "Spawner sweep starting over {SpawnerCount} spawner(s)")]
    public static partial void SpawnerSweepStarting(ILogger logger, int spawnerCount);

    [LoggerMessage(EventId = 2022, Level = LogLevel.Error,
        Message = "Spawner sweep threw; the next sweep runs as scheduled")]
    public static partial void SpawnerSweepFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2023, Level = LogLevel.Debug,
        Message = "Spawner {SpawnerId}: zone '{ZoneKey}' not found")]
    public static partial void SpawnerZoneNotFound(ILogger logger, Guid spawnerId, string zoneKey);

    [LoggerMessage(EventId = 2024, Level = LogLevel.Debug,
        Message = "Spawner {SpawnerId}: world '{WorldKey}' not found")]
    public static partial void SpawnerWorldNotFound(ILogger logger, Guid spawnerId, string worldKey);

    [LoggerMessage(EventId = 2025, Level = LogLevel.Debug,
        Message = "Spawner {SpawnerId} ('{TemplateKey}'): {CurrentCount}/{TargetCount} across {RoomCount} room(s)")]
    public static partial void SpawnerPopulation(
        ILogger logger, Guid spawnerId, string templateKey, int currentCount, int targetCount, int roomCount);

    // RoomKey is passed as itself rather than pre-formatted: the generator emits its IsEnabled
    // check before touching arguments, so at the default level this costs nothing at all.
    [LoggerMessage(EventId = 2026, Level = LogLevel.Debug,
        Message = "Spawner {SpawnerId}: spawned '{TemplateKey}' in {Room}")]
    public static partial void SpawnerSpawned(ILogger logger, Guid spawnerId, string templateKey, RoomKey room);

    [LoggerMessage(EventId = 2027, Level = LogLevel.Error,
        Message = "Spawner {SpawnerId} threw while spawning mobs; the sweep continues")]
    public static partial void MobSpawnerFailed(ILogger logger, Guid spawnerId, Exception exception);
}
