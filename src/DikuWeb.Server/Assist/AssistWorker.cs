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
    AssistWarmUp warmUp,
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
                AssistLog.Failed(logger, request.Subject, e);
                queue.Failed(id, e.Message);
            }
        }
    }

    private async Task RunAsync(Guid id, AssistRequest request, CancellationToken stoppingToken)
    {
        // Waited for BEFORE the job's own clock starts, which is the whole point of doing it here.
        //
        // Ollama serves one request at a time by design (OLLAMA_NUM_PARALLEL is 1, so that parallel
        // slots cannot divide the window and fragment the prefix cache). A draft submitted while
        // the canon is still being cached would therefore sit behind the warm-up inside Ollama —
        // and on the deployment that is about half an hour, against a job timeout of ten. The job
        // would be killed for the crime of arriving early, having done nothing wrong and having not
        // yet been looked at.
        if (!warmUp.Ready.IsCompleted)
        {
            queue.Warming(id);
            await warmUp.Ready.WaitAsync(stoppingToken).ConfigureAwait(false);
        }

        queue.Started(id);

        // Its own timeout, linked to shutdown, so a model that has stopped answering cannot hold
        // the single worker forever and starve every job behind it.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.Value.TimeoutSeconds)));

        using var scope = scopes.CreateScope();

        var assistant = scope.ServiceProvider.GetRequiredService<IContentAssistant>();

        switch (request)
        {
            case RoomDraftRequest room:
                await RoomAsync(id, room, scope, assistant, timeout.Token).ConfigureAwait(false);
                break;

            case ProseDraftRequest prose:
                await ProseAsync(id, prose, scope, assistant, timeout.Token).ConfigureAwait(false);
                break;

            default:
                // Unreachable while AssistRequest has two subtypes, and a job that silently never
                // finished would be worse than one that says what happened.
                queue.Failed(id, $"Nothing here knows how to draft a {request.GetType().Name}.");
                break;
        }
    }

    private async Task RoomAsync(
        Guid id,
        RoomDraftRequest request,
        IServiceScope scope,
        IContentAssistant assistant,
        CancellationToken cancellationToken)
    {
        var contexts = scope.ServiceProvider.GetRequiredService<IZoneContextSource>();

        var context = await contexts.ForZoneAsync(request.ZoneKey, cancellationToken).ConfigureAwait(false);

        if (context is null)
        {
            queue.Failed(id, $"There is no zone '{request.ZoneKey}'.");
            return;
        }

        var draft = await assistant.DraftRoomAsync(request, context, cancellationToken).ConfigureAwait(false);

        var destinations = context.RoomKeys
            .Where(k => !string.Equals(k, request.RoomKey, StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Reviewed before it is stored, so the warnings reach the builder with the draft rather
        // than after they have already read it and believed it.
        queue.Succeeded(id, draft, RoomDraftReview.Review(draft, destinations));
    }

    private async Task ProseAsync(
        Guid id,
        ProseDraftRequest request,
        IServiceScope scope,
        IContentAssistant assistant,
        CancellationToken cancellationToken)
    {
        var contexts = scope.ServiceProvider.GetRequiredService<IProseContextSource>();

        var context = await contexts.ForAsync(request.Kind, request.Key, cancellationToken)
            .ConfigureAwait(false);

        // The entity has to exist first: its numbers are the context, and prose written without
        // them is prose that will contradict the thing it describes.
        if (context is null)
        {
            queue.Failed(
                id,
                $"There is no {request.Kind.ToString().ToLowerInvariant()} '{request.Key}'. "
                + "Create it first — the assist describes what exists rather than inventing it.");
            return;
        }

        var draft = await assistant.DraftProseAsync(request, context, cancellationToken)
            .ConfigureAwait(false);

        queue.Succeeded(id, draft, ProseDraftReview.Review(draft, request.Kind, context.Exemplars));
    }
}
