using DikuWeb.Domain.Spawning;

namespace DikuWeb.Engine.Spawning;

/// <summary>
/// How many instances a spawner may place on this sweep (PLAN.md §4.8).
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the whole of "some things are rarer than others".</b> Before it, the sweep refilled
/// every spawner to its target on every pass, so the real respawn delay for everything in the game
/// was zero to fifteen seconds — a player could stand in one room and kill the same mob forever
/// instead of going to find another. <c>Spawner.RespawnSeconds</c> existed and was read by nothing
/// (BUGS.md #17).
/// </para>
/// <para>
/// Three rules, and each one is a decision rather than an implementation detail:
/// </para>
/// <list type="number">
///   <item>
///     <b>A cold spawner fills to target at once.</b> A world that has just loaded is not a world
///     where everything has just died, and a fresh server whose bosses were an hour away would be
///     an empty one. The consequence is deliberate and worth knowing: <b>a restart re-arms
///     everything</b>, so a boss killed a minute before a restart is standing again after it.
///     Persisting the timers would fix that and would mean a shutdown could bank an hour of
///     someone else's wait, which is worse.
///   </item>
///   <item>
///     <b>After that, one replacement per window.</b> Not a refill to target. Clearing a room of
///     four at sixty seconds buys four minutes, not sixty seconds — which is the difference between
///     a room worth leaving and a room worth camping.
///   </item>
///   <item>
///     <b>The clock starts when the sweep notices, not when the thing died.</b> Nothing tells this
///     class about a death; it counts heads. So the delay is what was authored plus however much of
///     the fifteen-second sweep was left — immaterial at a minute, invisible at an hour, and the
///     price of not threading spawner bookkeeping through every death and pickup path.
///   </item>
/// </list>
/// <para>
/// State is per process and unsynchronised, because the sweep runs on the loop thread and there is
/// one of it.
/// </para>
/// </remarks>
internal sealed class SpawnerSchedule
{
    private sealed class Pending
    {
        /// <summary>Whether this spawner has ever been brought up to its target.</summary>
        public bool Filled;

        /// <summary>The pulse the next single replacement is allowed on, or null when none is due.</summary>
        public long? DueAtPulse;
    }

    private readonly Dictionary<Guid, Pending> _state = [];

    /// <summary>
    /// How many instances to place for this spawner right now.
    /// </summary>
    /// <param name="currentCount">
    /// What the caller counted. Mobs count across the world and items count across the spawner's
    /// rooms — two different questions (see <c>SpawnerSystem</c>), and this does not care which,
    /// only whether the answer is short of the target.
    /// </param>
    public int Allowance(Spawner spawner, int currentCount, long pulse)
    {
        ArgumentNullException.ThrowIfNull(spawner);

        if (!_state.TryGetValue(spawner.Id, out var state))
        {
            state = new Pending();
            _state[spawner.Id] = state;
        }

        var deficit = spawner.TargetCount - currentCount;

        if (deficit <= 0)
        {
            // At or over target. Whatever it was waiting for has arrived by another route - a
            // builder spawned one by hand, or the target was lowered - so the wait is off.
            state.Filled = true;
            state.DueAtPulse = null;
            return 0;
        }

        if (!state.Filled)
        {
            state.Filled = true;
            state.DueAtPulse = null;
            return deficit;
        }

        // Set on the sweep that first sees the shortfall, then fallen through to immediately: a
        // spawner authored at zero seconds places on the same sweep rather than on the next one.
        state.DueAtPulse ??= pulse + DelayPulses(spawner);

        if (pulse < state.DueAtPulse)
        {
            return 0;
        }

        // Still short after this one? Then the next replacement waits its own window.
        state.DueAtPulse = deficit > 1 ? pulse + DelayPulses(spawner) : null;
        return 1;
    }

    /// <summary>
    /// Forgets spawners that no longer exist, so a long-running world does not accumulate state
    /// for content a builder deleted.
    /// </summary>
    public void Retain(IEnumerable<Guid> liveSpawnerIds)
    {
        ArgumentNullException.ThrowIfNull(liveSpawnerIds);

        var live = liveSpawnerIds.ToHashSet();

        foreach (var id in _state.Keys.Where(id => !live.Contains(id)).ToList())
        {
            _state.Remove(id);
        }
    }

    /// <summary>
    /// The authored delay in pulses. Negative seconds are treated as zero rather than refused,
    /// because a spawner is content and §7.4 degrades rather than throwing.
    /// </summary>
    private static long DelayPulses(Spawner spawner) =>
        Math.Max(0, spawner.RespawnSeconds) * 4L;
}
