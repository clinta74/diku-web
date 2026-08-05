using System.Text.Json;
using DikuWeb.Domain.Accounts;
using DikuWeb.Engine;
using DikuWeb.Persistence;
using DikuWeb.Persistence.Seeding;
using DikuWeb.Server;
using DikuWeb.Server.Admin;
using DikuWeb.Server.Auth;
using DikuWeb.Server.Building;
using DikuWeb.Server.Characters;
using DikuWeb.Server.Game;
using DikuWeb.Server.Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Configuration
// ---------------------------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("DikuWeb")
    ?? throw new InvalidOperationException(
        "Connection string 'DikuWeb' is not configured. See README.md for local setup.");

// ---------------------------------------------------------------------------
// Services
// ---------------------------------------------------------------------------
builder.Services.AddDikuWebPersistence(connectionString);

builder.Services.AddDikuWebEngine(options =>
{
    options.StartingRoom = StarterWorldSeeder.StartingRoom;
});

// The Engine does not reference EF Core, so the Server supplies both adapters.
builder.Services.AddSingleton<IWorldSource, EfWorldSource>();
builder.Services.AddSingleton<CharacterSaveQueue>();
builder.Services.AddSingleton<ICharacterSaveQueue>(sp => sp.GetRequiredService<CharacterSaveQueue>());
builder.Services.AddHostedService<CharacterSaveWorker>();

builder.Services.AddSingleton<WorldWriteQueue>();
builder.Services.AddSingleton<IWorldWriteQueue>(sp => sp.GetRequiredService<WorldWriteQueue>());
builder.Services.AddHostedService<WorldWriteWorker>();

// Account administration (PLAN.md §7.7). Same shape: the loop enqueues, a worker does the
// database work and sends the answer back through the loop.
builder.Services.AddSingleton<AccountAdminQueue>();
builder.Services.AddSingleton<IAccountAdminQueue>(sp => sp.GetRequiredService<AccountAdminQueue>());
builder.Services.AddHostedService<AccountAdminWorker>();
builder.Services.AddScoped<AccountAdminService>();

// Sessions are keyed by character, so one account can play several at once. The cap exists
// because each character holds an open SSE connection and a ring buffer.
var sessionOptions = new SessionRegistryOptions();
builder.Configuration.GetSection("Sessions").Bind(sessionOptions);
builder.Services.AddSingleton(sessionOptions);
builder.Services.AddSingleton<SessionRegistry>();

builder.Services.AddSingleton<IPasswordHasher<Account>, PasswordHasher<Account>>();

var authOptions = new AuthOptions();
builder.Configuration.GetSection("Auth").Bind(authOptions);
builder.Services.AddSingleton(authOptions);

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "dikuweb.session";

        // PLAN.md §3.2: HttpOnly because the browser's native EventSource cannot send an
        // Authorization header, so the cookie is the only credential the stream can carry.
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;

        // SameAsRequest keeps development over plain HTTP working while still setting
        // Secure in production. The dev client proxies through Vite so the browser sees one
        // origin, which is what lets SameSite=Lax behave identically in both environments.
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true;

        // This is an API, not a site with a login page. Without these the framework answers
        // an unauthenticated fetch with a 302 to /Account/Login, which reaches the client as
        // a confusing 200 containing HTML.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };

        // Re-checks role and ban state against the database on an interval (PLAN.md §7.7).
        // Without it a demotion, or a ban on someone already connected, does nothing until the
        // cookie expires a fortnight later.
        options.Events.OnValidatePrincipal = PrincipalRevalidator.ValidateAsync;
    });

builder.Services.AddAuthorization(options => options.AddDikuWebPolicies());

// The world builder (PLAN.md §7). Queries and writes are scoped because they own a DbContext;
// the throttle is a singleton because it is per-account state that must outlive a request.
builder.Services.AddScoped<BuilderQueries>();
builder.Services.AddScoped<WorldWriter>();
builder.Services.AddScoped<WorldEditor>();
builder.Services.AddSingleton<DigThrottle>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<DikuWebDbContext>("database", tags: ["ready"]);

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// ---------------------------------------------------------------------------
// Endpoints
// ---------------------------------------------------------------------------
app.MapAuthEndpoints();
app.MapCharacterEndpoints();
app.MapGameEndpoints();
app.MapBuilderEndpoints();
app.MapAdminEndpoints();

// Liveness: is the process up? Deliberately runs no checks, so a database outage never
// causes an orchestrator to restart an otherwise healthy server.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => false,
});

// Readiness: can we actually serve? Includes the database.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = WriteHealthResponseAsync,
});

// ---------------------------------------------------------------------------
// Startup
// ---------------------------------------------------------------------------
var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("DikuWeb.Server");
ServerLog.Starting(logger, app.Environment.EnvironmentName);

var csb = new NpgsqlConnectionStringBuilder(connectionString);
ServerLog.DatabaseConfigured(logger, csb.Host ?? "(unset)", csb.Database ?? "(unset)");

// Migrating on startup is a development convenience only. In production, concurrent
// instances racing to migrate is a real hazard, so deploys apply migrations explicitly
// as their own step (PLAN.md §6).
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<DikuWebDbContext>();

    ServerLog.ApplyingMigrations(logger);
    await db.Database.MigrateAsync();

    if (await StarterWorldSeeder.SeedAsync(db))
    {
        ServerLog.SeededStarterWorld(logger, StarterWorldSeeder.StartingRoom.ToString());
    }
    else
    {
        ServerLog.SeedSkipped(logger);
    }
}

await app.RunAsync();

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

static Task WriteHealthResponseAsync(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";

    var payload = new
    {
        status = report.Status.ToString(),
        durationMs = report.TotalDuration.TotalMilliseconds,
        checks = report.Entries.Select(e => new
        {
            name = e.Key,
            status = e.Value.Status.ToString(),
            // Exception text is deliberately omitted: this endpoint is reachable from
            // outside and must not leak connection strings or stack traces.
            description = e.Value.Description,
        }),
    };

    return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
}

/// <summary>
/// Exposed so WebApplicationFactory in DikuWeb.Server.Tests can find the entry point.
/// </summary>
public partial class Program;
