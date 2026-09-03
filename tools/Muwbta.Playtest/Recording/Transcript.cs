using System.Collections.Concurrent;
using System.Diagnostics;

namespace Muwbta.Playtest.Recording;

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
public sealed class Transcript(int? byteBudget = null)
{
    private readonly ConcurrentQueue<TranscriptEntry> _entries = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private int _met;
    private int _unmet;
    private long _bytes;

    /// <summary>When the run began, for correlating this record with server logs.</summary>
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;

    /// <summary>Everything still held, oldest first.</summary>
    /// <remarks>
    /// "Still held" rather than "recorded" because a bounded transcript drops its oldest lines —
    /// see the capacity argument. An unbounded one, which is every ordinary playtest, holds
    /// everything.
    /// </remarks>
    public IReadOnlyList<TranscriptEntry> Entries => [.. _entries];

    /// <summary>Expectations met, counted as they happened.</summary>
    /// <remarks>
    /// Counted rather than tallied from <see cref="Entries"/> at the end, because under a capacity
    /// the observation that proved a thing may already have been dropped — a total recounted from
    /// what survives would fall as the run got longer, which is the opposite of what it means.
    /// </remarks>
    public int Met => Volatile.Read(ref _met);

    /// <inheritdoc cref="Met"/>
    public int Unmet => Volatile.Read(ref _unmet);

    public TranscriptEntry Add(string actor, EntryKind kind, string text, string? raw = null)
    {
        var entry = new TranscriptEntry(
            DateTimeOffset.UtcNow, _clock.Elapsed, actor, kind, text)
        {
            // A bounded transcript keeps prose and drops payloads. Raw is the SSE frame the text
            // was rendered from, so holding both stores every line twice - and nothing reads it
            // except the JSON report, which a load session never produces. Halving the cost of the
            // record is worth more than a duplicate nobody opens.
            Raw = byteBudget is null ? raw : null,
        };

        Append(entry);
        return entry;
    }

    public TranscriptEntry AddObservation(string actor, string expectation, bool met)
    {
        var entry = new TranscriptEntry(
            DateTimeOffset.UtcNow, _clock.Elapsed, actor, EntryKind.Observation, expectation)
        {
            Met = met,
        };

        if (met)
        {
            Interlocked.Increment(ref _met);
        }
        else
        {
            Interlocked.Increment(ref _unmet);
        }

        Append(entry);
        return entry;
    }

    /// <summary>
    /// Appends, dropping the oldest lines to stay inside a byte budget when one was set.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Bounded only for load sessions.</b> An ordinary playtest transcript is the artefact — a
    /// reviewer reads it — so it keeps everything. Two hundred of them do not get read, and the
    /// record is then the largest thing in the process: a run holding every line spends its memory
    /// on scrollback nobody will open and ends up measuring its own garbage collector rather than
    /// the server.
    /// </para>
    /// <para>
    /// <b>Budgeted in bytes rather than in lines, which was learned the hard way.</b> A cap of two
    /// thousand entries sounds generous and is not a bound at all: one <c>who</c> reply with two
    /// hundred names online, or one room description listing two hundred occupants, is kilobytes
    /// on its own. At seventy sessions that came to twelve megabytes each and the apparatus died
    /// of an OutOfMemoryException with the measurement half-taken. A byte budget is self-tuning —
    /// a session drowning in fan-out keeps fewer lines, a quiet one keeps more — and it bounds the
    /// thing that actually runs out.
    /// </para>
    /// </remarks>
    private void Append(TranscriptEntry entry)
    {
        _entries.Enqueue(entry);

        if (byteBudget is not { } budget)
        {
            return;
        }

        Interlocked.Add(ref _bytes, Weigh(entry));

        while (Volatile.Read(ref _bytes) > budget && _entries.TryDequeue(out var dropped))
        {
            Interlocked.Add(ref _bytes, -Weigh(dropped));
        }
    }

    /// <summary>
    /// Roughly what one entry costs, in bytes.
    /// </summary>
    /// <remarks>
    /// Two bytes a character for UTF-16, plus a flat allowance for the object header, the two
    /// timestamps and the references. Approximate on purpose: the budget is a guard against
    /// running out of memory, not an accounting record, and an exact answer would cost more to
    /// compute than the slack it would recover.
    /// </remarks>
    private static long Weigh(TranscriptEntry entry) =>
        128 + (2L * (entry.Text.Length + (entry.Raw?.Length ?? 0) + entry.Actor.Length));

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
