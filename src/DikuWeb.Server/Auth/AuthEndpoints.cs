using System.Security.Claims;
using System.Text.RegularExpressions;
using DikuWeb.Domain.Accounts;
using DikuWeb.Persistence;
using DikuWeb.Server.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DikuWeb.Server.Auth;

public sealed record RegisterRequest(string Email, string Username, string Password);

public sealed record LoginRequest(string Username, string Password);

public sealed record AccountResponse(Guid Id, string Username, string Email, string Role);

public static partial class AuthEndpoints
{
    private const int MinPasswordLength = 8;

    public static void MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/auth");

        // The only surface a stranger can reach, so the only one where the limit is tight
        // (PLAN.md §8, Phase 6). Partitioned by address, since there is no account yet to
        // partition by.
        group.MapPost("/register", RegisterAsync)
            .RequireRateLimiting(RateLimiting.Auth);

        group.MapPost("/login", LoginAsync)
            .RequireRateLimiting(RateLimiting.Auth);

        // Left alone: it reads the cookie the caller already has and touches no database row a
        // stranger could guess at.
        group.MapGet("/me", MeAsync);

        // Cast required. LogoutAsync's signature is Func<HttpContext, Task<IResult>>, which
        // also matches RequestDelegate (Func<HttpContext, Task>) because Task<IResult> is a
        // Task. Without the cast the compiler binds the RequestDelegate overload and the
        // IResult is silently discarded - the endpoint would return an empty 200 and never
        // run the result. ASP0016 catches this.
        group.MapPost("/logout", (Delegate)LogoutAsync);
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        DikuWebDbContext db,
        IPasswordHasher<Account> hasher,
        HttpContext http,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        // Captured into locals so the compiler's null analysis carries through to the
        // Account below. The record's properties are non-nullable but JSON deserialisation
        // can still hand us nulls.
        var username = request.Username;
        var email = request.Email;
        var password = request.Password;

        if (username is null || !UsernamePattern().IsMatch(username))
        {
            return Results.BadRequest(new { error = "Username must be 3-24 letters, digits, or underscores." });
        }

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@', StringComparison.Ordinal))
        {
            return Results.BadRequest(new { error = "A valid email address is required." });
        }

        if (password is null || password.Length < MinPasswordLength)
        {
            return Results.BadRequest(new { error = $"Password must be at least {MinPasswordLength} characters." });
        }

        // citext makes these comparisons case-insensitive in the database, so this check
        // and the unique index agree with each other.
        var taken = await db.Accounts.AnyAsync(
            a => a.Username == username || a.Email == email,
            cancellationToken);

        if (taken)
        {
            return Results.Conflict(new { error = "That username or email is already registered." });
        }

        // The first account on an empty database becomes Admin. Without this there is no way
        // to reach the world builder at all on a fresh install except by hand-editing a row,
        // and a first-run step that requires SQL is a first-run step nobody completes.
        // Safe by construction: it can only ever happen once, when there is nobody to escalate
        // over (PLAN.md Phase 2 - roles).
        var isFirstAccount = !await db.Accounts.AnyAsync(cancellationToken);

        var account = new Account
        {
            Email = email,
            Username = username,
            PasswordHash = string.Empty,
            Role = isFirstAccount ? AccountRole.Admin : AccountRole.Player,
            CreatedAt = clock.GetUtcNow(),
        };

        account.PasswordHash = hasher.HashPassword(account, password);

        db.Accounts.Add(account);
        await db.SaveChangesAsync(cancellationToken);

        if (isFirstAccount)
        {
            ServerLog.FirstAccountPromoted(
                http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("DikuWeb.Server"),
                account.Username);
        }

        await SignInAsync(http, account);
        return Results.Ok(ToResponse(account));
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        DikuWebDbContext db,
        IPasswordHasher<Account> hasher,
        HttpContext http,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var account = await db.Accounts
            .FirstOrDefaultAsync(a => a.Username == request.Username, cancellationToken);

        // Deliberately the same response for "no such user" and "wrong password", so the
        // endpoint cannot be used to enumerate which usernames exist.
        if (account is null
            || hasher.VerifyHashedPassword(account, account.PasswordHash, request.Password ?? string.Empty)
                == PasswordVerificationResult.Failed)
        {
            return Results.Unauthorized();
        }

        if (account.IsBanned)
        {
            return Results.Json(
                new { error = "This account is banned.", reason = account.BanReason },
                statusCode: StatusCodes.Status403Forbidden);
        }

        account.LastLoginAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        await SignInAsync(http, account);
        return Results.Ok(ToResponse(account));
    }

    private static async Task<IResult> LogoutAsync(HttpContext http)
    {
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.NoContent();
    }

    private static async Task<IResult> MeAsync(
        HttpContext http,
        DikuWebDbContext db,
        CancellationToken cancellationToken)
    {
        if (!http.TryGetAccountId(out var accountId))
        {
            return Results.Unauthorized();
        }

        var account = await db.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);

        return account is null ? Results.Unauthorized() : Results.Ok(ToResponse(account));
    }

    private static Task SignInAsync(HttpContext http, Account account) =>
        http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            BuildPrincipal(account.Id, account.Username, account.Role));

    /// <summary>
    /// The one place the claim set is defined. Shared with
    /// <see cref="PrincipalRevalidator"/>, which rebuilds it when a role changes mid-session -
    /// two constructions of the same principal would drift the moment a claim was added.
    /// </summary>
    internal static ClaimsPrincipal BuildPrincipal(Guid id, string username, AccountRole role) =>
        new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, id.ToString()),
                new Claim(ClaimTypes.Name, username),
                new Claim(ClaimTypes.Role, role.ToString()),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme));

    private static AccountResponse ToResponse(Account account) =>
        new(account.Id, account.Username, account.Email, account.Role.ToString());

    [GeneratedRegex("^[A-Za-z0-9_]{3,24}$")]
    private static partial Regex UsernamePattern();
}

public static class HttpContextExtensions
{
    public static bool TryGetAccountId(this HttpContext http, out Guid accountId)
    {
        ArgumentNullException.ThrowIfNull(http);

        accountId = default;
        var claim = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return claim is not null && Guid.TryParse(claim, out accountId);
    }
}
