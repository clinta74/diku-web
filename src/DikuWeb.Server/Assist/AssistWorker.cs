using DikuWeb.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DikuWeb.Server.Assist;

/// <summary>Assembles what a zone can teach the model about itself.</summary>
public interface IZoneContextSource
{
    Task<ZoneContext?> ForZoneAsync(string zoneKey, CancellationToken cancellationToken);
}

/// <summary>Reads the zone and its rooms straight out of Postgres.</summary>
/// <remarks>
/// <b>Exemplars are the longest descriptions in the zone, not the first three.</b> A zone in
/// progress is full of stubs - <c>RoomFlags.Unfinished</c> exists precisely because half-written
/// rooms are normal - and teaching the voice from three one-line stubs would teach the model to
/// write stubs. Length is a crude proxy for "somebody finished this one", and it is available
/// without another column.
/// </remarks>
public sealed class EfZoneContextSource(DikuWebDbContext db) : IZoneContextSource
{
    private const int Exemplars = 3;

    /// <summary>Below this a description is a placeholder rather than an example.</summary>
    private const int WorthLearningFrom = 120;

    public async Task<ZoneContext?> ForZoneAsync(string zoneKey, CancellationToken cancellationToken)
    {
        var zone = await db.Zones
            .AsNoTracking()
            .FirstOrDefaultAsync(z => z.Key == zoneKey, cancellationToken)
            .ConfigureAwait(false);

        if (zone is null)
        {
            return null;
        }

        var rooms = await db.Rooms
            .AsNoTracking()
            .Where(r => r.ZoneKey == zoneKey)
            .Select(r => new { r.Key, r.Title, r.Description })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var exemplars = rooms
            .Where(r => r.Description.Length >= WorthLearningFrom)
            .OrderByDescending(r => r.Description.Length)
            .Take(Exemplars)
            .Select(r => new RoomExemplar(r.Title, r.Description))
            .ToList();

        return new ZoneContext(
            zone.Name,
            zone.Description,
            [.. rooms.Select(r => r.Key.ToString()).Order(StringComparer.Ordinal)],
            exemplars);
    }
}

/// <summary>
/// The single worker that turns queued requests into drafts.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here goes near the game loop.</b> This is an ordinary hosted service on its own
/// thread; PLAN.md §2.1's single-writer rule is about the world, and a draft touches nothing in it.
/// The only shared resource is the model, and there is exactly one of those, which is why there is
/// exactly one of these.
/// </para>
/// <para>
/// <b>A failed job is a finished job.</b> Every exception becomes a message on the job and the
/// worker carries on: an assistant that stops answering because one zone key was wrong would take
/// the feature down for everybody, and the failure it is protecting against - the model being slow,
/// absent, or refusing - is the expected case rather than the exceptional one.
/// </para>
/// </remarks>
public sealed class AssistWorker(
    AssistQueue queue,
    IServiceScopeFactory scopes,
    IOptions<AssistOptions> options,
    ILogger<AssistWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var (id, request) in queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await RunAsync(id, request, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutting down. The job dies with the process, which is what "in memory" means.
                break;
            }
            catch (Exception e)
            {
                AssistLog.Failed(logger, request.RoomKey, e);
                queue.Failed(id, e.Message);
            }
        }
    }

    private async Task RunAsync(Guid id, RoomDraftRequest request, CancellationToken stoppingToken)
    {
        queue.Started(id);

        // Its own timeout, linked to shutdown, so a model that has stopped answering cannot hold
        // the single worker forever and starve every job behind it.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.Value.TimeoutSeconds)));

        using var scope = scopes.CreateScope();

        var contexts = scope.ServiceProvider.GetRequiredService<IZoneContextSource>();
        var assistant = scope.ServiceProvider.GetRequiredService<IContentAssistant>();

        var context = await contexts.ForZoneAsync(request.ZoneKey, timeout.Token).ConfigureAwait(false);

        if (context is null)
        {
            queue.Failed(id, $"There is no zone '{request.ZoneKey}'.");
            return;
        }

        var draft = await assistant.DraftRoomAsync(request, context, timeout.Token).ConfigureAwait(false);

        var destinations = context.RoomKeys
            .Where(k => !string.Equals(k, request.RoomKey, StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Reviewed before it is stored, so the warnings reach the builder with the draft rather
        // than after they have already read it and believed it.
        queue.Succeeded(id, draft, RoomDraftReview.Review(draft, destinations));
    }
}
