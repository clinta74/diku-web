using System.Text.Json;
using DikuWeb.Domain.Abilities;
using DikuWeb.Domain.Abilities.Effects;
using DikuWeb.Domain.Accounts;
using DikuWeb.Engine;
using DikuWeb.Persistence;
using DikuWeb.Persistence.Converters;
using DikuWeb.Persistence.Seeding;
using DikuWeb.Server;
using DikuWeb.Server.Admin;
using DikuWeb.Server.Auth;
using DikuWeb.Server.Building;
using DikuWeb.Server.Characters;
using DikuWeb.Server.Game;
using DikuWeb.Server.Infrastructure;
using DikuWeb.Server.Infrastructure.Repositories;
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
var connectionString = BuildConnectionString(builder.Configuration);

// ---------------------------------------------------------------------------
// Services
// ---------------------------------------------------------------------------
builder.Services.AddDikuWebPersistence(connectionString);

builder.Services.AddDikuWebEngine(options =>
{
    options.StartingRoom = StarterWorldSeeder.StartingRoom;
});

// The Engine does not reference EF Core, so the Server supplies adapters.
builder.Services.AddSingleton<IWorldSource, EfWorldSource>();
builder.Services.AddSingleton<IMobTemplateRepository, EfMobTemplateRepository>();
builder.Services.AddSingleton<IItemTemplateRepository, EfItemTemplateRepository>();
builder.Services.AddSingleton<ISpawnerRepository, EfSpawnerRepository>();
builder.Services.AddSingleton<IAbilityRepository, EfAbilityRepository>();
builder.Services.AddSingleton<IQuestRepository, EfQuestRepository>();
builder.Services.AddSingleton<ICharacterQuestRepository, EfCharacterQuestRepository>();

builder.Services.AddSingleton<EffectRegistry>();

builder.Services.AddSingleton<CharacterSaveQueue>();
builder.Services.AddSingleton<ICharacterSaveQueue>(sp => sp.GetRequiredService<CharacterSaveQueue>());
builder.Services.AddHostedService<CharacterSaveWorker>();

builder.Services.AddSingleton<ItemSaveQueue>();
builder.Services.AddSingleton<IItemSaveQueue>(sp => sp.GetRequiredService<ItemSaveQueue>());
builder.Services.AddHostedService<ItemSaveQueueWorker>();

builder.Services.AddSingleton<CharacterQuestSaveQueue>();
builder.Services.AddSingleton<ICharacterQuestSaveQueue>(sp => sp.GetRequiredService<CharacterQuestSaveQueue>());
builder.Services.AddHostedService<CharacterQuestSaveWorker>();

builder.Services.AddSingleton<WorldWriteQueue>();
builder.Services.AddSingleton<IWorldWriteQueue>(sp => sp.GetRequiredService<WorldWriteQueue>());
builder.Services.AddHostedService<WorldWriteWorker>();

// Shutdown handler: ensures all pending saves and mutations are flushed before server stops
builder.Services.AddHostedService<ShutdownFlushService>();

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

// Configure JSON serialization to handle enum string values for the builder API
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new TemplateKindConverter());
});

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

// Migrate on startup in every environment. The game loop is single-writer by design (§2.1)
// with no backplane to share world state, so this process cannot be scaled horizontally
// anyway - there is no second instance to race with. EF also takes an exclusive advisory
// lock for the duration, so even an accidental second instance waits rather than colliding.
// The tradeoff accepted here: a bad migration fails the deploy at startup rather than at a
// separate gate, so rollback is "deploy the previous image", not "stop the migration job".
{
    var factory = app.Services.GetRequiredService<IDbContextFactory<DikuWebDbContext>>();
    await using var db = await factory.CreateDbContextAsync();

    ServerLog.ApplyingMigrations(logger);

    // Zero disables the wait entirely, which is what the tests that point at an unreachable
    // database use so they still fail in a second rather than sixty.
    var retryBudget = TimeSpan.FromSeconds(
        app.Configuration.GetValue("Database:MigrationRetryBudgetSeconds", 60d));

    await StartupMigrator.RunAsync(
        db.Database.MigrateAsync,
        StartupMigrator.RetryPolicy.For(retryBudget),
        logger,
        TimeProvider.System);

    // Seeding stays development-only: it writes starter content, which is a fixture, not schema.
    if (app.Environment.IsDevelopment())
    {
        if (await StarterWorldSeeder.SeedAsync(db))
        {
            ServerLog.SeededStarterWorld(logger, StarterWorldSeeder.StartingRoom.ToString());
        }
        else
        {
            ServerLog.SeedSkipped(logger);
        }
    }
}

await app.RunAsync();

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

static string BuildConnectionString(IConfiguration config)
{
    // Try to build from individual connection parts first (docker-compose / container orchestration)
    var host = config["DatabaseConnection:Host"];
    var port = config["DatabaseConnection:Port"];
    var database = config["DatabaseConnection:Database"];
    var user = config["DatabaseConnection:User"];
    var password = config["DatabaseConnection:Password"];

    if (!string.IsNullOrEmpty(host) && !string.IsNullOrEmpty(database))
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = string.IsNullOrEmpty(port) ? 5432 : int.Parse(port),
            Database = database,
            Username = user ?? "postgres",
            Password = password,
            Pooling = true,
            MaxPoolSize = 20,
            ApplicationName = "dikuweb-web",
            CommandTimeout = 30,
            Timezone = "UTC",
        };
        return builder.ConnectionString;
    }

    // Fallback to appsettings/user secrets connection string (development)
    var connectionString = config.GetConnectionString("DikuWeb");
    if (!string.IsNullOrEmpty(connectionString))
    {
        return connectionString;
    }

    throw new InvalidOperationException(
        "Connection string not configured. Set either:\n" +
        "  1. DatabaseConnection:* environment variables (docker), or\n" +
        "  2. ConnectionStrings:DikuWeb in appsettings.json / user secrets");
}

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
