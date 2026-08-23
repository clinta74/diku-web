using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace DikuWeb.Server.Assist;

/// <summary>
/// The jobs waiting to be drafted, and the ones recently drafted.
/// </summary>
/// <remarks>
/// <para>
/// <b>A queue rather than a request, because of a measurement.</b> Generation runs at 1.3-1.8
/// tokens a second and a room description is about 230 tokens, so one draft is roughly three
/// minutes on a fast desktop and expected to be worse on the deployment's four cores. No browser
/// waits three minutes, and no request thread should be held open for it either.
/// </para>
/// <para>
/// <b>One worker, and the depth is small.</b> <c>OLLAMA_NUM_PARALLEL</c> is 1 - deliberately, so
/// that parallel slots cannot divide the context window and fragment the prefix cache - so a second
/// concurrent request would not be served any sooner, it would just be waiting somewhere else.
/// Refusing the ninth job says something true about when it would be answered; accepting it would
/// not.
/// </para>
/// <para>
/// <b>In memory, and lost on restart.</b> A draft is a suggestion nobody has accepted yet: the
/// cost of losing one is asking again, and the cost of persisting them is a table, a migration and
/// a sweeper for rows nobody will ever read. If drafts ever become something a builder returns to
/// tomorrow, that is the point to revisit this - and it will be a different feature.
/// </para>
/// </remarks>
public sealed class AssistQueue
{
    private readonly ConcurrentDictionary<Guid, AssistJob> _jobs = new();
    private readonly Channel<(Guid Id, AssistRequest Request)> _channel;
    private readonly AssistOptions _options;
    private readonly TimeProvider _time;

    /// <param name="options">Depth and retention.</param>
    /// <param name="time">
    /// The clock. Injected because the sweep is otherwise untestable: retention is clamped to at
    /// least a minute, so "already expired" cannot be expressed through configuration and a test
    /// would have to sleep for one. That clamp is right - a zero or negative retention would throw
    /// away a draft the moment it finished - so the clock moves instead.
    /// </param>
    public AssistQueue(IOptions<AssistOptions> options, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _time = time ?? TimeProvider.System;

        // Bounded and rejecting. DropWrite would accept the request and never run it, which is the
        // one behaviour worse than refusing: the builder waits for an answer that was discarded at
        // the door.
        _channel = Channel.CreateBounded<(Guid, AssistRequest)>(
            new BoundedChannelOptions(Math.Max(1, _options.MaxQueued))
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
            });
    }

    /// <summary>Everything the worker has yet to pick up.</summary>
    internal ChannelReader<(Guid Id, AssistRequest Request)> Reader => _channel.Reader;

    /// <summary>
    /// Queues a draft, or refuses when there is no room.
    /// </summary>
    /// <returns>The job id, or null when the queue is full.</returns>
    public Guid? TryEnqueue(AssistRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        Sweep();

        var id = Guid.NewGuid();

        // TryWrite rather than WriteAsync: this runs on a request thread, and the honest answer to
        // a full queue is "not now" rather than holding the connection until a slot frees.
        if (!_channel.Writer.TryWrite((id, request)))
        {
            return null;
        }

        _jobs[id] = new AssistJob(
            id, AssistJobState.Queued, _time.GetUtcNow(), null, null, null, null, null, []);

        return id;
    }

    /// <summary>The job, or null when it never existed or has been swept.</summary>
    public AssistJob? Find(Guid id) => _jobs.GetValueOrDefault(id);

    /// <summary>
    /// Watchers of one job, so a change can be pushed rather than asked for.
    /// </summary>
    /// <remarks>
    /// Keyed by job because that is the granularity anybody cares about: a builder waiting on their
    /// own draft has no interest in anyone else's. Modelled on <c>BuilderChangeFeed</c>, which is
    /// the same shape one level up.
    /// </remarks>
    private readonly ConcurrentDictionary<Guid, Channel<AssistJob>> _watchers = new();

    /// <summary>
    /// Watches one job until the returned handle is disposed.
    /// </summary>
    /// <remarks>
    /// The current state is written immediately, before any change happens. That is what makes a
    /// dropped SSE connection self-healing: <c>EventSource</c> reconnects on its own, and the
    /// reconnection is told where things stand rather than waiting for the next transition - which
    /// for a job that finished during the gap would never come.
    /// </remarks>
    public IDisposable Watch(Guid id, out ChannelReader<AssistJob> reader)
    {
        // Latest-wins and depth 1: a watcher that falls behind wants the current state, not a
        // history of states it missed. There are at most four in a job's life anyway.
        var channel = Channel.CreateBounded<AssistJob>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        var key = Guid.NewGuid();
        _watchers[key] = channel;
        reader = channel.Reader;

        if (_jobs.TryGetValue(id, out var job))
        {
            channel.Writer.TryWrite(job);
        }

        return new Watcher(this, key, id);
    }

    /// <summary>Which job each watcher is watching.</summary>
    private readonly ConcurrentDictionary<Guid, Guid> _watching = new();

    private sealed class Watcher : IDisposable
    {
        private readonly AssistQueue _queue;
        private readonly Guid _key;

        public Watcher(AssistQueue queue, Guid key, Guid jobId)
        {
            _queue = queue;
            _key = key;
            queue._watching[key] = jobId;
        }

        public void Dispose()
        {
            _queue._watching.TryRemove(_key, out _);

            if (_queue._watchers.TryRemove(_key, out var channel))
            {
                channel.Writer.TryComplete();
            }
        }
    }

    private void Publish(AssistJob job)
    {
        foreach (var (key, watched) in _watching)
        {
            if (watched == job.Id && _watchers.TryGetValue(key, out var channel))
            {
                channel.Writer.TryWrite(job);
            }
        }
    }

    internal void Warming(Guid id) => Update(id, job => job with
    {
        State = AssistJobState.Warming,
    });

    internal void Started(Guid id) => Update(id, job => job with
    {
        State = AssistJobState.Running,
        StartedAt = _time.GetUtcNow(),
    });

    internal void Succeeded(Guid id, RoomDraft draft, IReadOnlyList<string> warnings) =>
        Update(id, job => job with
        {
            State = AssistJobState.Succeeded,
            FinishedAt = _time.GetUtcNow(),
            Draft = draft,
            Warnings = warnings,
        });

    internal void Succeeded(Guid id, ProseDraft prose, IReadOnlyList<string> warnings) =>
        Update(id, job => job with
        {
            State = AssistJobState.Succeeded,
            FinishedAt = _time.GetUtcNow(),
            Prose = prose,
            Warnings = warnings,
        });

    internal void Failed(Guid id, string error) => Update(id, job => job with
    {
        State = AssistJobState.Failed,
        FinishedAt = _time.GetUtcNow(),
        Error = error,
    });

    private void Update(Guid id, Func<AssistJob, AssistJob> change)
    {
        if (_jobs.TryGetValue(id, out var job))
        {
            var updated = change(job);
            _jobs[id] = updated;
            Publish(updated);
        }
    }

    /// <summary>
    /// Forgets finished jobs older than the retention window.
    /// </summary>
    /// <remarks>
    /// Swept on enqueue rather than on a timer: the dictionary only grows when somebody asks for a
    /// draft, so the moment somebody asks is exactly when it is worth looking. A timer would be a
    /// second thing to start, stop, and reason about at shutdown for no gain.
    /// </remarks>
    private void Sweep()
    {
        var cutoff = _time.GetUtcNow().AddMinutes(-Math.Max(1, _options.JobRetentionMinutes));

        foreach (var (id, job) in _jobs)
        {
            if (job.FinishedAt is { } finished && finished < cutoff)
            {
                _jobs.TryRemove(id, out _);
            }
        }
    }
}
