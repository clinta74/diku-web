using System.Collections.Concurrent;
using System.Diagnostics;

namespace DikuWeb.Playtest.Recording;

/// <summary>What kind of thing happened. Drives how a line is rendered and filtered.</summary>
public enum EntryKind
{
    /// <summary>The apparatus said something the player would have typed.</summary>
    Sent,

    /// <summary>Game prose — the scrollback, and the only thing a player actually reads.</summary>
    Text,

    /// <summary>Health, focus, stamina, level, gold.</summary>
    Vitals,

    /// <summary>The client talking about the connection, or an admin action reported in.</summary>
    Sys,

    /// <summary>Rendering data — the ASCII grid and what stands on it.</summary>
    Frame,

    /// <summary>A line the plan author wrote, marking what the next stretch is meant to show.</summary>
    Note,

    /// <summary>A step boundary.</summary>
    Step,

    /// <summary>An expectation, met or unmet.</summary>
    Observation,

    /// <summary>The apparatus talking about itself: logins, timeouts, retries.</summary>
    Meta,
}

/// <summary>
/// One line of the record, stamped when it happened.
/// </summary>
/// <param name="Elapsed">
/// Time since the run began, not wall-clock. A reviewer reading a multi-actor transcript is asking
/// "what did the other one see when this landed", and a relative clock answers that at a glance
/// where an ISO timestamp does not. <see cref="At"/> keeps the absolute time for correlating with
/// server logs.
/// </param>
public sealed record TranscriptEntry(
    DateTimeOffset At,
    TimeSpan Elapsed,
    string Actor,
    EntryKind Kind,
    string Text)
{
    /// <summary>The raw SSE payload, where this entry came from one. Null otherwise.</summary>
    public string? Raw { get; init; }

    /// <summary>Set on <see cref="EntryKind.Observation"/>: whether the expectation was met.</summary>
    public bool? Met { get; init; }
}

/// <summary>
/// The record of one run: every actor's output, interleaved, in arrival order.
/// </summary>
/// <remarks>
/// One shared log rather than one per actor, with the actor as a column. The per-actor views are
/// projections of this, which is what guarantees they agree — two independently appended logs would
/// drift the moment one of them dropped an entry, and a multi-actor transcript whose columns
/// disagree about ordering is worse than no transcript at all.
///
/// Appends come from every actor's stream pump at once, hence the concurrent queue.
/// </remarks>
public sealed class Transcript
{
    private readonly ConcurrentQueue<TranscriptEntry> _entries = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    /// <summary>When the run began, for correlating this record with server logs.</summary>
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;

    /// <summary>Everything recorded so far, oldest first.</summary>
    public IReadOnlyList<TranscriptEntry> Entries => [.. _entries];

    public TranscriptEntry Add(string actor, EntryKind kind, string text, string? raw = null)
    {
        var entry = new TranscriptEntry(
            DateTimeOffset.UtcNow, _clock.Elapsed, actor, kind, text)
        {
            Raw = raw,
        };

        _entries.Enqueue(entry);
        return entry;
    }

    public TranscriptEntry AddObservation(string actor, string expectation, bool met)
    {
        var entry = new TranscriptEntry(
            DateTimeOffset.UtcNow, _clock.Elapsed, actor, EntryKind.Observation, expectation)
        {
            Met = met,
        };

        _entries.Enqueue(entry);
        return entry;
    }

    /// <summary>
    /// Everything one actor produced since <paramref name="since"/>, for a wait or an expectation
    /// to read.
    /// </summary>
    /// <remarks>
    /// Scoped by time rather than by a cursor the caller holds, because the pump is still appending
    /// while this runs: a cursor handed out before a command was posted would be stale by the time
    /// the answer arrived, and the step would judge itself against output from before it acted.
    /// </remarks>
    public IReadOnlyList<TranscriptEntry> Since(string actor, TimeSpan since) =>
        [.. _entries.Where(e =>
            e.Elapsed >= since &&
            string.Equals(e.Actor, actor, StringComparison.Ordinal))];

    /// <summary>Time since the run began, the same clock every entry is stamped from.</summary>
    public TimeSpan Now => _clock.Elapsed;
}
