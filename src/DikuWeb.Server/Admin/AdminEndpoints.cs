using DikuWeb.Domain.Accounts;
using DikuWeb.Engine;
using DikuWeb.Engine.Protocol;
using DikuWeb.Server.Auth;

namespace DikuWeb.Server.Admin;

public sealed record SetRoleRequest(string? Role);

/// <summary>
/// Role administration (PLAN.md §7.7). This is the real interface; the in-game <c>promote</c>
/// verb is a convenience over it.
/// </summary>
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/api/admin").RequireAuthorization(Policies.Admin);

        group.MapGet("/accounts", SearchAsync);
        group.MapGet("/accounts/{username}", GetAsync);
        group.MapPatch("/accounts/{username}/role", SetRoleAsync);
    }

    private static async Task<IResult> SearchAsync(
        string? q,
        int? limit,
        AccountAdminService accounts,
        CancellationToken ct) =>
        Results.Ok(await accounts.SearchAsync(q, limit ?? 50, ct));

    private static async Task<IResult> GetAsync(
        string username,
        AccountAdminService accounts,
        CancellationToken ct) =>
        await accounts.FindAsync(username, ct) is { } account
            ? Results.Ok(account)
            : Results.NotFound();

    private static async Task<IResult> SetRoleAsync(
        string username,
        SetRoleRequest request,
        AccountAdminService accounts,
        GameGateway gateway,
        HttpContext http,
        CancellationToken ct)
    {
        if (!http.TryGetAccountId(out var actorId))
        {
            return Results.Unauthorized();
        }

        if (!Enum.TryParse<AccountRole>(request.Role, ignoreCase: true, out var role)
            || !Enum.IsDefined(role))
        {
            return Results.BadRequest(new
            {
                error = "Role must be one of: Player, Builder, Moderator, Admin.",
            });
        }

        var result = await accounts.SetRoleAsync(actorId, username, role, ct);

        if (result.Outcome == RoleChangeOutcome.Changed && result.TargetAccountId is { } id)
        {
            // Live, for a character already in the world. The cookie is handled separately by
            // revalidation (§7.7) - this only updates the copy the game loop holds.
            gateway.TrySubmit(new SetActorRole { AccountId = id, Role = role });
        }

        return result.Outcome switch
        {
            RoleChangeOutcome.NoSuchAccount => Results.NotFound(new { error = result.Message }),
            RoleChangeOutcome.WouldDemoteSelf => Results.Conflict(new { error = result.Message }),
            _ => Results.Ok(await accounts.FindAsync(username, ct)),
        };
    }
}
