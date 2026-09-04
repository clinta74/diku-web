using System.Collections.Concurrent;

namespace Muwbta.Server.Auth;

/// <summary>
/// Slows repeated failed sign-ins against one account, whoever is making them.
/// </summary>
/// <remarks>
/// <b>What the address limit does not cover.</b> The auth rate limit is per caller address, which
/// bounds what one machine can try. It bounds nothing about one <em>account</em>: a guesser with a
/// hundred addresses gets a hundred budgets against the same password, and the eight-character
/// floor is not a defence against that on its own. This is the per-account half.
///
/// <b>A growing delay, not a lockout.</b> After <see cref="AuthOptions.LoginFailuresBeforeBackoff"/>
/// failures the next attempt waits <see cref="AuthOptions.LoginBackoffSeconds"/>, and each further
/// failure doubles it up to <see cref="AuthOptions.LoginBackoffMaxSeconds"/>. A person who has
/// forgotten their password meets a thirty-second pause on the fifth try and a fifteen-minute one
/// only after nine more; a guesser meets the ceiling almost immediately and stays there. A
/// success clears everything, and so does an admin lifting it — which is what the cap is for.
/// Without one, whoever hammers an account owns it, because its real owner can never get back in.
///
/// <b>Keyed on the name as typed, whether or not it exists.</b> Throttling only real accounts
/// would answer "does this account exist" by whether the throttle ever fires. An unknown name is
/// slowed exactly like a known one.
///
/// <b>In memory, and single-instance.</b> The same argument the rate limiter makes: a
/// single-writer loop cannot run two instances anyway (PLAN.md §6.1), so there is no second
/// process for a shared table to coordinate with, and a restart forgiving every fuse is the
/// right failure mode. Bounded by pruning entries whose last failure is older than the ceiling.
/// </remarks>
public sealed class LoginThrottle(AuthOptions options, TimeProvider clock)
{
    /// <summary>Above this many entries, a failure also sweeps out the stale ones.</summary>
    private const int PruneAbove = 10_000;

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How long the caller has to wait before this name may try again, or null.</summary>
    public TimeSpan? RetryAfter(string username)
    {
        ArgumentNullException.ThrowIfNull(username);

        var until = LockedUntil(username);
        return until is { } t ? t - clock.GetUtcNow() : null;
    }

    /// <summary>When the current pause ends, or null when there is none. For the admin panel.</summary>
    public DateTimeOffset? LockedUntil(string username)
    {
        ArgumentNullException.ThrowIfNull(username);

        if (!_entries.TryGetValue(username, out var entry))
        {
            return null;
        }

        lock (entry)
        {
            return entry.LockedUntil > clock.GetUtcNow() ? entry.LockedUntil : null;
        }
    }

    /// <summary>
    /// One more wrong password against this name. True when this failure started a pause, so the
    /// caller can count the event; a pause that merely lengthened is not a new one.
    /// </summary>
    public bool RecordFailure(string username)
    {
        ArgumentNullException.ThrowIfNull(username);

        if (options.LoginFailuresBeforeBackoff <= 0)
        {
            // Zero disables it - the same convention the revalidation interval uses.
            return false;
        }

        var now = clock.GetUtcNow();
        var entry = _entries.GetOrAdd(username, _ => new Entry());

        lock (entry)
        {
            // A failure older than the ceiling starts the count over. A password forgotten last
            // month should not shorten this month's fuse.
            if (now - entry.LastFailure > Ceiling)
            {
                entry.Failures = 0;
            }

            entry.Failures++;
            entry.LastFailure = now;

            var over = entry.Failures - options.LoginFailuresBeforeBackoff;
            if (over >= 0)
            {
                var seconds = Math.Min(
                    options.LoginBackoffMaxSeconds,
                    options.LoginBackoffSeconds * Math.Pow(2, over));

                entry.LockedUntil = now.AddSeconds(seconds);
            }

            if (_entries.Count > PruneAbove)
            {
                Prune(now);
            }

            return over == 0;
        }
    }

    /// <summary>The right password: everything against this name is forgiven.</summary>
    public void RecordSuccess(string username)
    {
        ArgumentNullException.ThrowIfNull(username);
        _entries.TryRemove(username, out _);
    }

    /// <summary>An admin clearing the fuse. True when there was one to clear.</summary>
    public bool Lift(string username)
    {
        ArgumentNullException.ThrowIfNull(username);
        return _entries.TryRemove(username, out _);
    }

    private TimeSpan Ceiling => TimeSpan.FromSeconds(Math.Max(1, options.LoginBackoffMaxSeconds));

    private void Prune(DateTimeOffset now)
    {
        foreach (var (name, entry) in _entries)
        {
            bool stale;
            lock (entry)
            {
                stale = now - entry.LastFailure > Ceiling;
            }

            if (stale)
            {
                _entries.TryRemove(name, out _);
            }
        }
    }

    private sealed class Entry
    {
        public int Failures;
        public DateTimeOffset LastFailure = DateTimeOffset.MinValue;
        public DateTimeOffset LockedUntil = DateTimeOffset.MinValue;
    }
}
