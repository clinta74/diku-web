using System.Text;
using System.Text.Json;
using DikuWeb.Engine;
using DikuWeb.Engine.Protocol;
using DikuWeb.Persistence;
using DikuWeb.Server.Auth;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

namespace DikuWeb.Server.Game;

public sealed record EnterRequest(Guid CharacterId);

public sealed record CommandRequest(string Input);

public static class GameEndpoints
{
    /// <summary>PLAN.md §3.4: often enough that proxies never see the stream as idle.</summary>
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void MapGameEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/game").RequireAuthorization();

        group.MapPost("/enter", EnterAsync);
        group.MapGet("/stream", StreamAsync);
        group.MapPost("/command", SubmitCommand);
    }

    private static async Task<IResult> EnterAsync(
        EnterRequest request,
        HttpContext http,
        DikuWebDbContext db,
        SessionRegistry sessions,
        GameGateway gateway,
        CancellationToken cancellationToken)
    {
        if (!http.TryGetAccountId(out var accountId))
        {
            return Results.Unauthorized();
        }

        var character = await db.Characters.FirstOrDefaultAsync(
            c => c.Id == request.CharacterId && c.AccountId == accountId && c.DeletedAt == null,
            cancellationToken);

        if (character is null)
        {
            return Results.NotFound(new { error = "No such character on this account." });
        }

        var session = sessions.Open(accountId, character.Id, character.Name);

        var accepted = gateway.TrySubmit(new EnterWorld
        {
            SessionId = session.Id,
            Character = character,
            Output = session.Events.Writer,
        });

        if (!accepted)
        {
            sessions.Close(accountId);
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Ok(new { sessionId = session.Id, character = character.Name });
    }

    /// <summary>
    /// The long-lived SSE stream. Auth is the cookie: the browser's native EventSource
    /// cannot set request headers, which is why no token appears in this URL (PLAN.md §3.2).
    /// </summary>
    private static async Task StreamAsync(
        HttpContext http,
        SessionRegistry sessions,
        GameGateway gateway,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("DikuWeb.Server.Sse");

        if (!http.TryGetAccountId(out var accountId))
        {
            http.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var session = sessions.Find(accountId);
        if (session is null)
        {
            http.Response.StatusCode = StatusCodes.Status409Conflict;
            await http.Response.WriteAsync("Enter the world before opening a stream.");
            return;
        }

        var response = http.Response;
        response.Headers.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";

        // Without this, output sits in a buffer until it fills and the stream appears dead.
        // The header covers nginx; DisableBuffering covers Kestrel itself (PLAN.md §3.4).
        response.Headers["X-Accel-Buffering"] = "no";
        http.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        session.IsStreaming = true;

        try
        {
            await response.WriteAsync("retry: 3000\n\n", http.RequestAborted);
            await response.Body.FlushAsync(http.RequestAborted);

            await ReplayMissedEventsAsync(http, session);
            await PumpAsync(http, session);
        }
        catch (OperationCanceledException)
        {
            // The client went away. Expected, and the finally block handles it.
        }
        finally
        {
            session.IsStreaming = false;

            // The character does NOT leave the world here. It goes link-dead and stays put
            // for the grace window, so a dropped connection is survivable (PLAN.md §3.6).
            gateway.TrySubmit(new LeaveWorld
            {
                SessionId = session.Id,
                Reason = LeaveReason.LinkDead,
            });

            ServerLog.StreamClosed(logger, session.CharacterName);
        }
    }

    private static async Task ReplayMissedEventsAsync(HttpContext http, GameSession session)
    {
        var header = http.Request.Headers["Last-Event-ID"].ToString();
        if (!long.TryParse(header, out var lastSeen))
        {
            return;
        }

        foreach (var (id, gameEvent) in session.Replay(lastSeen))
        {
            await WriteEventAsync(http.Response, id, gameEvent, http.RequestAborted);
        }

        await http.Response.Body.FlushAsync(http.RequestAborted);
    }

    private static async Task PumpAsync(HttpContext http, GameSession session)
    {
        var reader = session.Events.Reader;
        var token = http.RequestAborted;

        while (!token.IsCancellationRequested)
        {
            // Wait for output, but never longer than the heartbeat interval. Timing out is
            // the normal, quiet case: it is how an idle stream stays alive through proxies.
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(token);
            wait.CancelAfter(HeartbeatInterval);

            bool hasData;
            try
            {
                hasData = await reader.WaitToReadAsync(wait.Token);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                await http.Response.WriteAsync(": ping\n\n", token);
                await http.Response.Body.FlushAsync(token);
                continue;
            }

            if (!hasData)
            {
                // The channel completed: the player quit or the server is shutting down.
                break;
            }

            while (reader.TryRead(out var gameEvent))
            {
                await WriteEventAsync(http.Response, session.Record(gameEvent), gameEvent, token);
            }

            await http.Response.Body.FlushAsync(token);
        }
    }

    private static async Task WriteEventAsync(
        HttpResponse response,
        long id,
        OutboundEvent gameEvent,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(gameEvent.Payload, JsonOptions);

        var frame = new StringBuilder()
            .Append("id: ").Append(id).Append('\n')
            .Append("event: ").Append(gameEvent.Type).Append('\n')
            .Append("data: ").Append(json).Append("\n\n")
            .ToString();

        await response.WriteAsync(frame, cancellationToken);
    }

    /// <summary>
    /// Returns 202 with an empty body. All output - including the result of this very
    /// command - arrives over the SSE stream, so there is exactly one ordered channel and
    /// the scrollback can never show events out of order (PLAN.md §3.3).
    /// </summary>
    private static IResult SubmitCommand(
        CommandRequest request,
        HttpContext http,
        SessionRegistry sessions,
        GameGateway gateway)
    {
        if (!http.TryGetAccountId(out var accountId))
        {
            return Results.Unauthorized();
        }

        var session = sessions.Find(accountId);
        if (session is null)
        {
            return Results.Conflict(new { error = "Not in the world." });
        }

        if (string.IsNullOrWhiteSpace(request.Input))
        {
            return Results.Accepted();
        }

        if (request.Input.Length > 512)
        {
            return Results.BadRequest(new { error = "Command too long." });
        }

        var accepted = gateway.TrySubmit(new PlayerCommand
        {
            SessionId = session.Id,
            Input = request.Input,
        });

        // A full inbound queue means the loop is already behind, so 429 is honest: retrying
        // immediately would only make it worse.
        return accepted ? Results.Accepted() : Results.StatusCode(StatusCodes.Status429TooManyRequests);
    }
}
