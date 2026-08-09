using System.Text;
using System.Text.Json;
using DikuWeb.Domain.Accounts;
using DikuWeb.Domain.Characters;
using DikuWeb.Engine;
using DikuWeb.Engine.Protocol;
using DikuWeb.Persistence;
using DikuWeb.Server.Auth;
using DikuWeb.Server.Infrastructure;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

namespace DikuWeb.Server.Game;

public sealed record CommandRequest(string Input);

/// <summary>
/// Game routes are scoped by character rather than by account, so one login can drive several
/// characters at once. The cookie still does the authorising; the id in the path only selects
/// which of the caller's own characters is meant, and ownership is checked on every request.
/// </summary>
public static class GameEndpoints
{
    /// <summary>PLAN.md §3.4: often enough that proxies never see the stream as idle.</summary>
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void MapGameEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/game").RequireAuthorization();

        group.MapGet("/sessions", ListSessions);
        group.MapPost("/{characterId:guid}/enter", EnterAsync);
        group.MapGet("/{characterId:guid}/stream", StreamAsync);
        group.MapPost("/{characterId:guid}/command", SubmitCommand);
        group.MapPost("/{characterId:guid}/leave", Leave);
    }

    /// <summary>Which of this account's characters are currently in the world.</summary>
    private static IResult ListSessions(HttpContext http, SessionRegistry sessions)
    {
        if (!http.TryGetAccountId(out var accountId))
        {
            return Results.Unauthorized();
        }

        return Results.Ok(sessions.ForAccount(accountId).Select(s => new
        {
            characterId = s.CharacterId,
            character = s.CharacterName,
            streaming = s.IsStreaming,
        }));
    }

    private static async Task<IResult> EnterAsync(
        Guid characterId,
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

        var character = await LoadOwnedCharacterAsync(db, accountId, characterId, cancellationToken);
        if (character is null)
        {
            return Results.NotFound(new { error = "No such character on this account." });
        }

        var result = sessions.Open(accountId, character.Id, character.Name);

        if (result.Outcome == SessionOpenOutcome.TooManyCharacters)
        {
            return Results.Json(
                new { error = "You already have the maximum number of characters in the world." },
                statusCode: StatusCodes.Status409Conflict);
        }

        var session = result.Session!;

        // Read here rather than in the Engine: the loop has no account store and could not
        // query one anyway (PLAN.md §2.1). This is what gates the in-game builder verbs, and -
        // since Phase 6 - whether anything this character says reaches anyone.
        var account = await db.Accounts
            .Where(a => a.Id == accountId)
            .Select(a => new { a.Role, a.MutedUntil })
            .FirstOrDefaultAsync(cancellationToken);

        // Load character's inventory and equipped items
        var items = await db.ItemInstances
            .Where(i => i.OwnerCharacterId == characterId)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // Quest progress, for the same reason as items: the loop cannot query a database
        // (PLAN.md §2.1), so anything it needs has to arrive on the EnterWorld message. Omitting
        // this is why every login previously started with an empty journal - progress was
        // written by CharacterQuestSaveQueue and then never read back, so a completed
        // non-repeatable quest could be turned in again indefinitely.
        var quests = await db.CharacterQuests
            .Where(q => q.CharacterId == characterId)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var accepted = gateway.TrySubmit(new EnterWorld
        {
            SessionId = session.Id,
            Character = character,
            Role = account?.Role ?? AccountRole.Player,
            MutedUntil = account?.MutedUntil,
            Output = session.Events.Writer,
            Items = items,
            Quests = quests,
        });

        if (!accepted)
        {
            sessions.Close(character.Id);
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Ok(new
        {
            sessionId = session.Id,
            characterId = character.Id,
            character = character.Name,
            reconnected = result.Outcome == SessionOpenOutcome.Replaced,
        });
    }

    /// <summary>
    /// The long-lived SSE stream. Auth is the cookie: the browser's native EventSource cannot
    /// set request headers, which is why no token appears in this URL (PLAN.md §3.2). The
    /// character id in the path is a selector, not a credential.
    /// </summary>
    private static async Task StreamAsync(
        Guid characterId,
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

        // Returns null both for "not in the world" and "belongs to someone else", so a
        // wrong-account probe cannot be distinguished from an unopened session.
        var session = sessions.Find(accountId, characterId);
        if (session is null)
        {
            http.Response.StatusCode = StatusCodes.Status409Conflict;
            await http.Response.WriteAsync("Enter the world with this character before opening a stream.");
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
                // The channel completed: the player quit, was replaced by another tab, or
                // the server is shutting down.
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
    /// command - arrives over that character's SSE stream, so there is exactly one ordered
    /// channel per character and the scrollback can never show events out of order
    /// (PLAN.md §3.3).
    /// </summary>
    private static IResult SubmitCommand(
        Guid characterId,
        CommandRequest request,
        HttpContext http,
        SessionRegistry sessions,
        GameGateway gateway)
    {
        if (!http.TryGetAccountId(out var accountId))
        {
            return Results.Unauthorized();
        }

        var session = sessions.Find(accountId, characterId);
        if (session is null)
        {
            return Results.Conflict(new { error = "That character is not in the world." });
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

    /// <summary>
    /// Explicitly removes a character from the world and frees its slot against the
    /// per-account cap, rather than waiting out the 90 s link-dead window.
    /// </summary>
    private static async Task<IResult> Leave(
        Guid characterId,
        HttpContext http,
        SessionRegistry sessions,
        GameGateway gateway,
        ICharacterSaveQueue characterQueue,
        IItemSaveQueue itemQueue,
        CancellationToken cancellationToken)
    {
        if (!http.TryGetAccountId(out var accountId))
        {
            return Results.Unauthorized();
        }

        var session = sessions.Find(accountId, characterId);
        if (session is null)
        {
            return Results.NoContent();
        }

        gateway.TrySubmit(new LeaveWorld
        {
            SessionId = session.Id,
            Reason = LeaveReason.Quit,
        });

        sessions.Close(characterId);

        // Flush pending saves before returning, with a 5-second timeout
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            await Task.WhenAll(
                characterQueue.FlushAsync(linked.Token),
                itemQueue.FlushAsync(linked.Token)
            );
        }
        catch (OperationCanceledException)
        {
            // Timeout is acceptable - we tried our best but don't want to hang the logout
        }

        return Results.NoContent();
    }

    private static Task<Character?> LoadOwnedCharacterAsync(
        DikuWebDbContext db,
        Guid accountId,
        Guid characterId,
        CancellationToken cancellationToken) =>
        db.Characters.FirstOrDefaultAsync(
            c => c.Id == characterId && c.AccountId == accountId && c.DeletedAt == null,
            cancellationToken);
}
