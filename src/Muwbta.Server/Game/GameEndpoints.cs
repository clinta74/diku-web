using System.Text;
using System.Text.Json;
using Muwbta.Domain.Accounts;
using Muwbta.Domain.Characters;
using Muwbta.Engine;
using Muwbta.Engine.Protocol;
using Muwbta.Persistence;
using Muwbta.Server.Auth;
using Muwbta.Server.Infrastructure;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

namespace Muwbta.Server.Game;

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

        // Charged to the same budget as a command, and for the same reason: entering costs three
        // queries and a loop message, leaving flushes two save queues and waits on them, and
        // neither was limited - so alternating the two in a loop was a cheap way to load the
        // database and the writers. The partition is by character, as for commands, so a player
        // reconnecting a few times is nowhere near it.
        group.MapPost("/{characterId:guid}/enter", EnterAsync)
            .RequireRateLimiting(RateLimiting.Commands);

        // The stream is deliberately unlimited. It is one long-lived request per character, so a
        // limiter would never fire on honest use and would be a way to break a session that had
        // reconnected a few times in quick succession.
        group.MapGet("/{characterId:guid}/stream", StreamAsync);

        // The one endpoint a player can hold down a key against, and the one that costs the
        // single-threaded loop its budget (PLAN.md §2.1).
        group.MapPost("/{characterId:guid}/command", SubmitCommand)
            .RequireRateLimiting(RateLimiting.Commands);

        group.MapPost("/{characterId:guid}/leave", Leave)
            .RequireRateLimiting(RateLimiting.Commands);

        // Deliberately not limited - see Heartbeat.
        group.MapPost("/{characterId:guid}/heartbeat", Heartbeat);
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
        MuwbtaDbContext db,
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
        ILoggerFactory loggerFactory,
        string? connection = null)
    {
        var logger = loggerFactory.CreateLogger("Muwbta.Server.Sse");

        if (!http.TryGetAccountId(out var accountId))
        {
            http.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        // Returns null both for "not in the world" and "belongs to someone else", so a
        // wrong-account probe cannot be distinguished from an unopened session.
        var live = sessions.Find(accountId, characterId);
        if (live is null)
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

        await response.WriteAsync("retry: 3000\n\n", http.RequestAborted);
        await response.Body.FlushAsync(http.RequestAborted);

        // Ownership is per character, not per session: entering builds a new session, so anything
        // remembered on one is forgotten at exactly the moment a second device arrives.
        var ownership = sessions.StreamFor(characterId);

        // A connection this character has already moved past belongs to a device that was turned
        // out while it was not looking, reconnecting on `EventSource`'s own three-second timer.
        // Turned away here rather than served, which is what stops the two devices trading the
        // stream back and forth for as long as both are open.
        //
        // Answered with a 200 carrying one `sys` frame rather than a 409: a status code has
        // nowhere to put a sentence, and the browser has to be *told* to stop rather than merely
        // stopped - a refusal it cannot read is a refusal it retries.
        if (ownership.IsSuperseded(connection))
        {
            await SendDisplacedAsync(http, live);
            ServerLog.StreamDisplaced(logger, live.CharacterName);
            return;
        }

        // Sole possession, so a character's channel is never read by two responses at once.
        using var claim = ownership.Claim(live, connection);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(
            http.RequestAborted, claim.Token);

        try
        {
            await ReplayMissedEventsAsync(http, live);
            await PumpAsync(http, live, stop.Token);
        }
        catch (OperationCanceledException)
        {
            // Either the client went away or a newer stream took over. Both are expected, and
            // which one it was is answered below rather than here.
        }

        if (claim.Displaced)
        {
            // Still connected, on another screen. No LeaveWorld: the character has not gone
            // link-dead, and saying it had would narrate them going still to a room they are
            // standing in and start a grace window against a live connection.
            await SendDisplacedAsync(http, live);
            ServerLog.StreamDisplaced(logger, live.CharacterName);
            return;
        }

        // The character does NOT leave the world here. It goes link-dead and stays put for the
        // grace window, so a dropped connection is survivable (PLAN.md §3.6).
        gateway.TrySubmit(new LeaveWorld
        {
            SessionId = live.Id,
            Reason = LeaveReason.LinkDead,
        });

        ServerLog.StreamClosed(logger, live.CharacterName);
    }

    /// <summary>
    /// Tells a connection it is no longer the live one, and closes without a retry hint.
    /// </summary>
    /// <remarks>
    /// Written on <see cref="HttpContext.RequestAborted"/> rather than on the pump's own token,
    /// which is cancelled by definition every time this is called. The event is deliberately not
    /// recorded into the ring buffer: it is about this connection rather than about the character,
    /// and replaying it to the *next* stream would tell a healthy connection it had been replaced.
    /// </remarks>
    private static async Task SendDisplacedAsync(HttpContext http, GameSession session)
    {
        try
        {
            await WriteEventAsync(
                http.Response,
                session.PeekNextEventId(),
                new OutboundEvent(
                    EventTypes.Sys,
                    new SysPayload(
                        "This character was opened somewhere else. This screen is no longer live.",
                        SysKinds.Displaced)),
                http.RequestAborted);

            await http.Response.Body.FlushAsync(http.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            // The displaced client had already gone. Nothing left to tell.
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

    /// <summary>
    /// Drains the session onto the wire until the client goes away or a newer stream takes over.
    /// </summary>
    /// <param name="token">
    /// The client's <c>RequestAborted</c> linked with this stream's claim, so a displaced
    /// response stops reading immediately rather than at its next heartbeat — the whole point
    /// being that it must not still be competing for events when its successor starts.
    /// </param>
    private static async Task PumpAsync(
        HttpContext http,
        GameSession session,
        CancellationToken token)
    {
        var reader = session.Events.Reader;

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

        // A command is evidence the client is there, quite apart from what it asks for. It does
        // not set SendsHeartbeats: typing is not a promise to keep typing, and holding a session
        // to a deadline it never agreed to is what the flag exists to prevent.
        session.Seen(TimeProvider.System.GetUtcNow(), heartbeat: false);

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
    /// The client saying it is still there.
    /// </summary>
    /// <remarks>
    /// <b>The only direction that proves anything.</b> Everything the server writes to a client
    /// goes into a kernel send buffer and succeeds whether or not anybody is listening, which is
    /// why a vanished client used to hold a live session for seventeen minutes (PLAN.md §11).
    /// A request arriving is the one signal that cannot be faked by a socket that has stopped
    /// working.
    ///
    /// Deliberately not the command endpoint: a player reading their scrollback sends no commands
    /// for minutes at a time and is entirely present. Deliberately not rate limited either — it is
    /// smaller and rarer than a command, and throttling the evidence of life into silence would
    /// invent the very failure this exists to detect.
    /// </remarks>
    private static IResult Heartbeat(
        Guid characterId,
        HttpContext http,
        SessionRegistry sessions,
        TimeProvider clock)
    {
        if (!http.TryGetAccountId(out var accountId))
        {
            return Results.Unauthorized();
        }

        var session = sessions.Find(accountId, characterId);

        if (session is null)
        {
            // Not in the world. The client should stop beating and re-enter; saying so is more
            // use than a 204 that lets it keep talking to nothing.
            return Results.Conflict(new { error = "That character is not in the world." });
        }

        session.Seen(clock.GetUtcNow(), heartbeat: true);
        return Results.NoContent();
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
        MuwbtaDbContext db,
        Guid accountId,
        Guid characterId,
        CancellationToken cancellationToken) =>
        db.Characters.FirstOrDefaultAsync(
            c => c.Id == characterId && c.AccountId == accountId && c.DeletedAt == null,
            cancellationToken);
}
