using System.Threading.Channels;
using Muwbta.Engine;
using Muwbta.Server.Building;

namespace Muwbta.Server.Infrastructure;

/// <summary>
/// The write side of the in-game builder command hand-off (PLAN.md §7.6). The loop calls
/// <see cref="Enqueue"/> and moves on; nothing here blocks it.
/// </summary>
public sealed class WorldWriteQueue : IWorldWriteQueue
{
    private readonly Channel<WorldWriteJob> _channel =
        Channel.CreateUnbounded<WorldWriteJob>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });

    public ChannelReader<WorldWriteJob> Reader => _channel.Reader;

    public void Enqueue(WorldWriteJob job) => _channel.Writer.TryWrite(job);

    public void Complete() => _channel.Writer.TryComplete();
}

/// <summary>
/// Drains content writes from in-game builder commands and applies them to Postgres, off the
/// game loop thread.
/// </summary>
/// <remarks>
/// Jobs are written one at a time and strictly in order. Batching would be wrong here: the
/// primitives inside a job are already ordered (a rename creates the new room before pointing
/// exits at it), and two jobs touching the same room must land in the order the loop applied
/// them or the database ends up describing a world that never existed.
/// </remarks>
public sealed class WorldWriteWorker(
    WorldWriteQueue queue,
    IServiceScopeFactory scopeFactory,
    BuilderChangeFeed feed,
    ILogger<WorldWriteWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The outer catch is not decoration. ReadAllAsync throws OperationCanceledException on
        // shutdown, and an exception escaping a BackgroundService is treated as a fault: the
        // host logs it as Critical and stops. When the host is *already* stopping, that
        // Critical goes to logging providers partway through disposal, and the resulting
        // ObjectDisposedException surfaces as a failure in whatever test happened to be
        // running. Normal shutdown must not look like a fault.
        try
        {
            await foreach (var job in queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var writer = scope.ServiceProvider.GetRequiredService<WorldWriter>();

                    await writer.WriteAsync(job.Changes, job.AccountId, stoppingToken);

                    // In-game builder verbs notify the feed too, so an edit typed at the prompt
                    // updates any open builder panel just like an edit made through the API.
                    foreach (var change in job.Changes)
                    {
                        feed.Publish(new BuilderChange(
                            change.EntityKind, change.EntityKey, WorldEditor.ActionOf(change), job.AccountId));
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A failed write must not kill the worker, or every later edit is lost
                    // too. Unlike the HTTP path (WorldEditor), there is nobody to hand a 500
                    // to and no reload is attempted - a reload here would silently revert an
                    // edit the builder can still see on screen, with no explanation.
                    var first = job.Changes.Count > 0 ? job.Changes[0] : null;
                    ServerLog.MutationNotPersisted(
                        logger, first?.EntityKind ?? "?", first?.EntityKey ?? "?", ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }
}
