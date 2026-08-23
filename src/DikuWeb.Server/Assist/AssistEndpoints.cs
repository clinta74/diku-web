using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using DikuWeb.Server.Auth;
using DikuWeb.Server.Infrastructure;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace DikuWeb.Server.Assist;

/// <summary>
/// Two endpoints: ask for a draft, and read one back.
/// </summary>
/// <remarks>
/// <b>Nothing here writes to the world.</b> PLAN.md §13's safety argument is that the assistant
/// proposes and never writes: the draft comes back as text, the builder edits it, and it reaches
/// the world through the same PATCH every other edit uses - so <c>WorldEditor</c> stays the only
/// path in and <c>content_audit</c> still records a person as the author. A bad suggestion is a
/// paragraph in a textarea that gets deleted.
/// </remarks>
public static class AssistEndpoints
{
    public static void MapAssistEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var options = routes.ServiceProvider.GetRequiredService<IOptions<AssistOptions>>().Value;

        // Not registered at all when disabled, so a server with no model behind it answers 404
        // rather than 500 or, worse, hanging for the timeout. The client already has to handle a
        // builder who cannot assist; this makes "cannot" the same shape as "not deployed".
        if (!options.Enabled)
        {
            return;
        }

        // Authorisation on the group, rate limiting deliberately NOT.
        //
        // The two kinds of call here cost wildly different things and a single group policy
        // charged them the same. Submitting occupies the only model this server has for minutes;
        // reading a job back is a dictionary lookup. With the assist policy on the group, the
        // client's own three-second poll spent the submission budget - one POST plus five GETs and
        // the sixth request was refused, while the draft it was asking about was still running and
        // went on to finish. The builder saw a failure for a job that had succeeded.
        //
        // So each endpoint says what it costs.
        var group = routes.MapGroup("/api/builder/assist")
            .RequireAuthorization(Policies.Builder);

        // Exists only when the assist does, so a 404 here is the client's answer to "is there a
        // model behind this server". One request per session beats a button that works for some
        // deployments and quietly 404s for others the first time somebody presses it.
        group.MapGet("/", () => Results.Ok(new { enabled = true }))
            .RequireRateLimiting(RateLimiting.Builder);

        group.MapPost("/rooms", (RoomDraftRequest request, AssistQueue queue) =>
        {
            if (string.IsNullOrWhiteSpace(request.ZoneKey) || string.IsNullOrWhiteSpace(request.RoomKey))
            {
                return Results.BadRequest("A draft needs a zone and a room key.");
            }

            var id = queue.TryEnqueue(request);

            // 429 rather than 503: the queue being full is this account's problem to wait out, and
            // it is the same thing the rate limiter says with the same status, so a client that
            // handles one handles both.
            return id is null
                ? Results.StatusCode(StatusCodes.Status429TooManyRequests)
                : Results.Accepted($"/api/builder/assist/rooms/{id}", new { id });
        }).RequireRateLimiting(RateLimiting.Assist);

        // Mobs, items and quests share one endpoint because they share one job: prose for a thing
        // that already exists. The kind is in the body rather than the path so the client has one
        // call to make and one shape to handle.
        group.MapPost("/prose", (ProseDraftRequest request, AssistQueue queue) =>
        {
            if (string.IsNullOrWhiteSpace(request.Key))
            {
                return Results.BadRequest("A draft needs the key of the thing to describe.");
            }

            var id = queue.TryEnqueue(request);

            return id is null
                ? Results.StatusCode(StatusCodes.Status429TooManyRequests)
                : Results.Accepted($"/api/builder/assist/rooms/{id}", new { id });
        }).RequireRateLimiting(RateLimiting.Assist);

        // One reader for both, because a job is a job: the client polls the same place whatever it
        // asked for, and reads whichever of `draft` or `prose` is filled in.
        // Push, rather than be asked.
        //
        // A draft takes minutes, so polling it meant twenty-odd requests per draft and an answer up
        // to three seconds later than it existed. One stream is one request that says something the
        // moment there is something to say, and it takes the rate limiter out of the question
        // entirely rather than negotiating with it. The builder already has this shape one level up
        // in BuilderChangeFeed; this is the same thing scoped to a single job.
        group.MapGet("/jobs/{id:guid}/stream", StreamJobAsync)
            .RequireRateLimiting(RateLimiting.Builder);

        // Kept as the plain way to ask, for anything that is not a browser and for a client whose
        // stream did not come up. The ordinary builder budget, because this is a dictionary lookup.
        group.MapGet("/rooms/{id:guid}", (Guid id, AssistQueue queue) =>
            queue.Find(id) is { } job ? Results.Ok(job) : Results.NotFound())
            .RequireRateLimiting(RateLimiting.Builder);
    }
    /// <summary>How often an idle stream says something, so proxies do not close it.</summary>
    /// <remarks>
    /// Twenty seconds, matching <c>BuilderEndpoints</c>. It matters more here: that feed is quiet
    /// between edits, and this one is guaranteed to be quiet for the minutes a draft takes.
    /// </remarks>
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(20);

    /// <summary>
    /// One job's state, as it changes, until it stops changing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The current state goes out immediately on connect</b>, before waiting for anything. That
    /// is what makes a dropped connection self-healing: <c>EventSource</c> reconnects by itself, and
    /// a reconnection that only waited for the <em>next</em> change would wait forever for a job
    /// that finished during the gap.
    /// </para>
    /// <para>
    /// <b>The stream closes itself when the job is done.</b> Left open, every draft a builder ever
    /// asked for would hold a connection for as long as the page did.
    /// </para>
    /// </remarks>
    private static async Task StreamJobAsync(HttpContext http, Guid id, AssistQueue queue, CancellationToken ct)
    {
        var response = http.Response;
        response.Headers.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";

        // Covers nginx (header) and Kestrel (DisableBuffering) - otherwise events sit buffered and
        // the stream looks dead, which for a three-minute job is indistinguishable from a hang.
        response.Headers["X-Accel-Buffering"] = "no";
        http.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        using var subscription = queue.Watch(id, out var reader);

        await response.WriteAsync("retry: 3000\n\n", ct).ConfigureAwait(false);
        await response.Body.FlushAsync(ct).ConfigureAwait(false);

        while (!ct.IsCancellationRequested)
        {
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
            wait.CancelAfter(Heartbeat);

            AssistJob job;

            try
            {
                job = await reader.ReadAsync(wait.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                await response.WriteAsync(": ping\n\n", ct).ConfigureAwait(false);
                await response.Body.FlushAsync(ct).ConfigureAwait(false);
                continue;
            }
            catch (ChannelClosedException)
            {
                break;
            }

            await response
                .WriteAsync($"data: {JsonSerializer.Serialize(job, AssistJson)}\n\n", ct)
                .ConfigureAwait(false);
            await response.Body.FlushAsync(ct).ConfigureAwait(false);

            if (job.State is AssistJobState.Succeeded or AssistJobState.Failed)
            {
                break;
            }
        }
    }

    /// <summary>
    /// camelCase and enums as names, matching what the endpoints above return.
    /// </summary>
    /// <remarks>
    /// Written by hand here rather than taken from the framework, because a stream body is composed
    /// rather than returned and so misses the pipeline that would otherwise apply these. Two shapes
    /// for one payload is exactly the drift worth avoiding: the client parses both with one reader.
    /// </remarks>
    private static readonly JsonSerializerOptions AssistJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
