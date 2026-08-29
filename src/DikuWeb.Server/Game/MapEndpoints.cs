namespace DikuWeb.Server.Game;

/// <summary>
/// The drawn maps, open to any player (PLAN.md §5).
/// </summary>
/// <remarks>
/// <para>
/// Authorised but not scoped to a character. A map is of the world rather than of anything one
/// character knows, so there is no id in the path and nothing here reads a session - which also
/// means opening the map cannot disturb a stream or count as activity.
/// </para>
/// <para>
/// <b>Deliberately not gated on where the player has been.</b> Every sheet this build carries is
/// listed to everyone who is logged in. A map of a realm you have not reached is a spoiler, and
/// showing it is still the right default: the maps are drawn from content that is public in the
/// repository, the Reaches are gated in play by attunement flags on the crossings rather than by
/// ignorance, and a player who cannot see where they are going is the thing this feature exists to
/// fix. If that changes, the filter belongs here and wants the character's flags, which is the
/// reason to leave this as one flat list rather than something already half-scoped.
/// </para>
/// </remarks>
public static class MapEndpoints
{
    public static void MapMapEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/maps").RequireAuthorization();

        group.MapGet("/", (MapSheets sheets) => Results.Ok(sheets.All));
        group.MapGet("/{world}", Sheet);
    }

    /// <summary>One realm's sheet, as SVG.</summary>
    /// <remarks>
    /// <see cref="Results.Bytes(byte[], string?, string?, bool, DateTimeOffset?, Microsoft.Net.Http.Headers.EntityTagHeaderValue?)"/>
    /// rather than writing the body, because it answers <c>If-None-Match</c> with a 304 on its own.
    /// These are the largest things the game serves and they change only on a deploy, so a client
    /// that already has one should be told so in a few hundred bytes rather than sent it again.
    /// </remarks>
    private static IResult Sheet(string world, MapSheets sheets)
    {
        if (!sheets.TryGet(world, out var svg, out var etag))
        {
            // Named, because the one way to reach this is a client asking for a world whose map
            // this build does not carry - and "which worlds have maps" is answerable from the
            // list beside it.
            return Results.NotFound(new { error = $"There is no map of '{world}'." });
        }

        return Results.Bytes(svg, "image/svg+xml", entityTag: etag);
    }
}
