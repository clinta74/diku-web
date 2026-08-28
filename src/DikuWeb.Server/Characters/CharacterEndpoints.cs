using System.Text.RegularExpressions;
using DikuWeb.Domain.Characters;
using DikuWeb.Engine;
using DikuWeb.Persistence;
using DikuWeb.Server.Auth;
using DikuWeb.Server.Game;
using Microsoft.EntityFrameworkCore;

namespace DikuWeb.Server.Characters;

public sealed record CreateCharacterRequest(string Name, string Path);

public sealed record CharacterResponse(
    Guid Id,
    string Name,
    string Path,
    int Level,
    long Xp,
    string RoomKey,
    DateTimeOffset? LastPlayedAt);

public static partial class CharacterEndpoints
{
    public static void MapCharacterEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/characters").RequireAuthorization();

        group.MapGet("/", ListAsync);
        group.MapPost("/", CreateAsync);
    }

    private static async Task<IResult> ListAsync(
        HttpContext http,
        DikuWebDbContext db,
        CancellationToken cancellationToken)
    {
        if (!http.TryGetAccountId(out var accountId))
        {
            return Results.Unauthorized();
        }

        var characters = await db.Characters
            .AsNoTracking()
            .Where(c => c.AccountId == accountId && c.DeletedAt == null)
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);

        return Results.Ok(characters.Select(ToResponse));
    }

    private static async Task<IResult> CreateAsync(
        CreateCharacterRequest request,
        HttpContext http,
        DikuWebDbContext db,
        EngineOptions engineOptions,
        SessionRegistryOptions sessionOptions,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!http.TryGetAccountId(out var accountId))
        {
            return Results.Unauthorized();
        }

        if (!NamePattern().IsMatch(request.Name ?? string.Empty))
        {
            return Results.BadRequest(new { error = "Name must be 3-16 letters." });
        }

        if (!Enum.TryParse<CharacterPath>(request.Path, ignoreCase: true, out var path))
        {
            var valid = string.Join(", ", Enum.GetNames<CharacterPath>());
            return Results.BadRequest(new { error = $"Path must be one of: {valid}." });
        }

        // Character names are globally unique, not per-account: two Kaels in one room would
        // make every targeted command ambiguous.
        if (await db.Characters.AnyAsync(c => c.Name == request.Name, cancellationToken))
        {
            return Results.Conflict(new { error = "That name is taken." });
        }

        // A roster cap, distinct from the concurrent-session cap the same options class carries.
        // Deleted characters do not count, matching the list this account can see - deleting one
        // is how a player frees a slot.
        //
        // Checked after the name and path, so somebody at the cap who also mistyped a name is told
        // about the name first. The cap is the answer they can do nothing about in this request,
        // and reporting it while a fixable problem is also present would send them away to delete
        // a character they did not need to.
        var existing = await db.Characters
            .CountAsync(c => c.AccountId == accountId && c.DeletedAt == null, cancellationToken);

        if (existing >= sessionOptions.MaxCharactersPerAccount)
        {
            return Results.Conflict(new
            {
                error = $"You already have {existing} characters, which is the limit. "
                    + "Delete one to make room.",
            });
        }

        var character = new Character
        {
            AccountId = accountId,
            Name = request.Name!,
            Path = path,
            Attributes = AttributeSet.Baseline,
            Vitals = Vitals.StartingFor(path),
            RoomKey = engineOptions.StartingRoom,
            CreatedAt = clock.GetUtcNow(),
        };

        db.Characters.Add(character);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/characters/{character.Id}", ToResponse(character));
    }

    private static CharacterResponse ToResponse(Character c) =>
        new(c.Id, c.Name, c.Path.ToString(), c.Level, c.Xp, c.RoomKey.ToString(), c.LastPlayedAt);

    [GeneratedRegex("^[A-Za-z]{3,16}$")]
    private static partial Regex NamePattern();
}
