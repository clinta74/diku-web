using DikuWeb.Engine.Protocol;
using DikuWeb.Engine.Time;
using DikuWeb.Engine.World;

namespace DikuWeb.Engine.Systems;

/// <summary>
/// How the Engine asks to be stopped (PLAN.md §8, Phase 6).
/// </summary>
/// <remarks>
/// An interface rather than a direct call to the host, because the Engine does not know it is
/// hosted. The Server implements it over <c>IHostApplicationLifetime</c>, and a test implements it
/// by recording that it was asked — which is the only way to assert a countdown without ending the
/// test run along with the world.
/// </remarks>
public interface IShutdownSignal
{
    /// <summary>Begins an orderly stop. Returns immediately; the host does the rest.</summary>
    void Stop();
}

/// <summary>
/// A shutdown announced in advance, so nobody loses a fight to a deploy (PLAN.md §8, Phase 6).
/// </summary>
/// <remarks>
/// Progress is already safe without this: characters autosave every five minutes and again on the
/// way out, and a graceful stop saves everyone (§11). What a warning protects is the half hour
/// somebody spent walking to a boss, which no save can give back.
///
/// The countdown lives on the loop rather than on a timer thread, so "how long is left" is
/// answered in pulses by the same clock everything else uses, and the world cannot be stopped
/// half-way through one.
/// </remarks>
public sealed class ShutdownSchedule(IGameClock clock, IShutdownSignal? signal = null)
{
    /// <summary>
    /// Seconds remaining at which the world is told, loudest last.
    /// </summary>
    /// <remarks>
    /// Sparse near the start and dense at the end, because the useful question changes: half an
    /// hour out you want to know whether to start something, and ten seconds out you want to know
    /// whether to stop reading and put your character somewhere sensible.
    /// </remarks>
    private static readonly int[] Milestones = [1800, 900, 600, 300, 120, 60, 30, 10, 5];

    private long? _dueAtPulse;
    private string? _reason;
    private readonly HashSet<int> _announced = [];

    /// <summary>Whether a shutdown is counting down right now.</summary>
    public bool IsScheduled => _dueAtPulse is not null;

    /// <summary>Whole seconds until the world stops, or null when nothing is scheduled.</summary>
    public int? SecondsRemaining => _dueAtPulse is { } due
        ? (int)Math.Max(0, (due - clock.CurrentPulse) / PulsesPerSecond)
        : null;

    private const int PulsesPerSecond = 4;

    /// <summary>
    /// Starts, or restarts, the countdown. A delay of zero stops the world on the next pulse.
    /// </summary>
    /// <remarks>
    /// Restarting rather than refusing when one is already running: an admin who scheduled ten
    /// minutes and then needs thirty is correcting themselves, and making them cancel first is a
    /// step that exists only to be annoying.
    /// </remarks>
    public void Schedule(int seconds, string? reason)
    {
        var delay = Math.Max(0, seconds);

        _dueAtPulse = clock.CurrentPulse + ((long)delay * PulsesPerSecond);
        _reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        _announced.Clear();

        // Milestones further out than the delay are marked as already said, so scheduling two
        // minutes does not immediately announce "thirty minutes remaining".
        foreach (var milestone in Milestones.Where(m => m > delay))
        {
            _announced.Add(milestone);
        }
    }

    /// <summary>Calls it off. Returns false when there was nothing to call off.</summary>
    public bool Cancel()
    {
        if (_dueAtPulse is null)
        {
            return false;
        }

        _dueAtPulse = null;
        _reason = null;
        _announced.Clear();
        return true;
    }

    /// <summary>
    /// Announces whatever is due and stops the world when the countdown runs out.
    /// </summary>
    /// <remarks>
    /// Called every pulse. Milestones are matched as "at or below, not yet said" rather than by
    /// equality, because a pulse takes 250 ms and a busy one can carry the clock past an exact
    /// second — a warning that only fires on the exact tick is one that eventually does not fire.
    /// </remarks>
    public void Tick(WorldState world)
    {
        ArgumentNullException.ThrowIfNull(world);

        if (_dueAtPulse is not { } due)
        {
            return;
        }

        var remaining = SecondsRemaining ?? 0;

        if (clock.CurrentPulse >= due)
        {
            Announce(world, "The world is closing now.");
            _dueAtPulse = null;
            signal?.Stop();
            return;
        }

        foreach (var milestone in Milestones)
        {
            if (remaining <= milestone && _announced.Add(milestone))
            {
                Announce(world, $"The world closes in {Describe(milestone)}.");
                break;
            }
        }
    }

    private void Announce(WorldState world, string headline)
    {
        var message = _reason is null ? headline : $"{headline} {_reason}";

        foreach (var actor in world.AllPlayers)
        {
            actor.SendSys(message, SysKinds.Warning);
        }
    }

    /// <summary>Reads as prose rather than as a number of seconds, once there are enough of them.</summary>
    public static string Describe(int seconds) => seconds switch
    {
        < 60 => $"{seconds} seconds",
        60 => "one minute",
        < 3600 when seconds % 60 == 0 => $"{seconds / 60} minutes",
        _ => $"{seconds / 60} minutes {seconds % 60} seconds",
    };
}
