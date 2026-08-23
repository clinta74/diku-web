using DikuWeb.Server.Auth;
using DikuWeb.Server.Infrastructure;
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

        var group = routes.MapGroup("/api/builder/assist")
            .RequireAuthorization(Policies.Builder)
            .RequireRateLimiting(RateLimiting.Assist);

        // Exists only when the assist does, so a 404 here is the client's answer to "is there a
        // model behind this server". One request per session beats a button that works for some
        // deployments and quietly 404s for others the first time somebody presses it.
        group.MapGet("/", () => Results.Ok(new { enabled = true }));

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
        });

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
        });

        // One reader for both, because a job is a job: the client polls the same place whatever it
        // asked for, and reads whichever of `draft` or `prose` is filled in.
        group.MapGet("/rooms/{id:guid}", (Guid id, AssistQueue queue) =>
            queue.Find(id) is { } job ? Results.Ok(job) : Results.NotFound());
    }
}
