using System.Collections.Concurrent;

namespace DikuWeb.Server.Building;

/// <summary>
/// Rate limits <c>dig</c> per account (PLAN.md §7.6).
/// </summary>
/// <remarks>
/// Guards against a held-down key sending a walk command forty times and carving forty rooms,
/// which is a plausible accident rather than an attack - the endpoint already requires the
/// Builder role, so the population being throttled is trusted. That is why this is a plain
/// in-memory last-dig timestamp rather than a distributed limiter: the failure mode it prevents
/// is a mess to clean up, not a breach.
/// </remarks>
public sealed class DigThrottle(TimeProvider clock)
{
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastDig = new();

    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(2);

    public bool TryDig(Guid accountId)
    {
        var now = clock.GetUtcNow();
        var allowed = true;

        _lastDig.AddOrUpdate(
            accountId,
            now,
            (_, previous) =>
            {
                if (now - previous < Interval)
                {
                    allowed = false;
                    return previous;
                }

                return now;
            });

        return allowed;
    }
}
