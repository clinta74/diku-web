using System.Text.Json;
using System.Text.Json.Serialization;
using DikuWeb.Domain.Abilities;
using DikuWeb.Domain.Abilities.Effects;
using DikuWeb.Domain.Accounts;
using DikuWeb.Domain.Worlds;
using DikuWeb.Engine;
using DikuWeb.Engine.Systems;
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

    // The Engine section was never read at all. docker-compose.prod.yml has set
    // Engine__LinkDeadGraceSeconds and Engine__StartingRoom since it was written, and both were
    // silently ignored — a knob that reads as configured and does nothing is worse than one that
    // does not exist, because nobody goes looking for it when the value appears not to apply.
    //
    // Read key by key rather than `.Bind(options)`, for two reasons that EngineConfigurationBinding
    // tests pin down, because both are the sort that change quietly under a dependency upgrade:
    //
    //  - Bind cannot set StartingRoom at all. RoomKey is a readonly record struct with no type
    //    converter, so the binder leaves the property untouched rather than reporting that it
    //    could not convert — which would have left Engine__StartingRoom exactly as it was, a
    //    setting that reads as configured and does nothing.
    //  - Bind throws on a scalar it cannot convert, so a typo in the grace window takes the host
    //    down at startup. Fail-fast is right for a value with no default; this one has a correct
    //    default already, and refusing to boot over it trades a cosmetic mistake for an outage.
    var engine = builder.Configuration.GetSection("Engine");

    if (int.TryParse(engine["LinkDeadGraceSeconds"], out var graceSeconds) && graceSeconds > 0)
    {
        options.LinkDeadGraceSeconds = graceSeconds;
    }

    if (RoomKey.TryParse(engine["StartingRoom"], out var startingRoom))
    {
        options.StartingRoom = startingRoom;
    }
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

// Lets an admin's `shutdown` reach the host. Registered here rather than in the Engine because
// stopping the process is the Server's business; the Engine only asks.
builder.Services.AddSingleton<IShutdownSignal, HostShutdownSignal>();

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

// Enums cross the wire as their names, not their ordinals.
//
// TemplateKindConverter came first and stays first, because converters are consulted in order and
// it is stricter than the general one: it refuses a number outright. The general converter behind
// it covers every other enum, which is what a bespoke converter per enum could not - an ability's
// Path, cost type, and targeting mode all serialised as integers until it was added, so the
// Abilities tab filtered `path === 'Warden'` against a 0 and rendered an empty list.
//
// Property-level [JsonConverter] attributes still win over both, so the nullable request enums
// keep NullableEnumConverter and its tolerance for an unrecognised value.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new TemplateKindConverter());
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// The world builder (PLAN.md §7). Queries and writes are scoped because they own a DbContext;
// the throttle is a singleton because it is per-account state that must outlive a request.
builder.Services.AddScoped<BuilderQueries>();
builder.Services.AddScoped<WorldWriter>();
builder.Services.AddScoped<WorldEditor>();
builder.Services.AddScoped<WorldExporter>();
builder.Services.AddScoped<WorldImporter>();
builder.Services.AddSingleton<DigThrottle>();
builder.Services.AddSingleton<BuilderChangeFeed>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<DikuWebDbContext>("database", tags: ["ready"]);

// Per-caller limits (§8, Phase 6). The numbers come from configuration so a deployment can adjust
// them without a rebuild — see RateLimiting.Options.
//
// DEPLOYMENT NOTE: the auth limit partitions by remote address, and behind the nginx front end
// (docker-compose.prod.yml) that address is the proxy's for every caller. Until forwarded headers
// are honoured, treat `RateLimits:AuthAttemptsPerMinute` as a site-wide cap rather than a
// per-visitor one, and set it accordingly.
builder.Services.AddDikuWebRateLimiting(builder.Configuration);

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// After authentication, because the command and builder limits are partitioned by who is calling
// and would otherwise see every request as anonymous — which would put every player in one shared
// bucket and let any one of them exhaust it for the rest.
app.UseRateLimiter();

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

    // Abilities are reconciled in every environment, unlike starter content. They are not a
    // fixture: the level table promises them, so a missing row is a character who cannot cast
    // what the game has already told them they know.
    var abilities = await StarterWorldSeeder.ReconcileAbilitiesAsync(db);
    if (abilities.ChangedAnything)
    {
        ServerLog.AbilitiesReconciled(logger, abilities.Added, abilities.Updated, abilities.Removed);
    }

    // Validated on every boot, because the reconcile no longer guarantees the table's shape. A row
    // can now arrive from a builder, from an import, or from a migration backfill that did not name
    // it, and every way an ability can be wrong is a way that fails in silence: the cast succeeds,
    // the cost is spent, and nothing happens.
    //
    // Reported, never fatal. A single mistyped effect key must not stop a server from starting -
    // that trades one broken ability for a world nobody can reach, which is the worse of the two by
    // a wide margin, and it is the same argument §7.4 makes about a broken world.
    await ValidateAbilitiesAsync(db, app.Services, logger);

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

/// <summary>
/// Checks every ability row against <see cref="AbilityValidator"/> and reports what it finds.
/// </summary>
/// <remarks>
/// This is the load-time half of the validator, and it exists because the reconcile stopped being
/// authoritative: nothing else now guarantees that what is in the table can actually run. An
/// ability naming an effect that does not exist, or missing the one parameter its effect reads,
/// costs its resource and does nothing at all — so without a line in the log the first report is a
/// player saying a spell feels weak.
/// </remarks>
static async Task ValidateAbilitiesAsync(
    DikuWebDbContext db,
    IServiceProvider services,
    ILogger logger)
{
    var rows = await db.Abilities.AsNoTracking().ToListAsync();
    var effects = services.GetRequiredService<EffectRegistry>();

    var problems = AbilityValidator.ValidateSet(rows, effects);
    var errors = 0;

    foreach (var problem in problems)
    {
        var key = string.IsNullOrEmpty(problem.Key) ? "progression" : problem.Key;

        if (problem.Severity == AbilityProblemSeverity.Error)
        {
            errors++;
            ServerLog.AbilityInvalid(logger, key, problem.Message);
        }
        else
        {
            ServerLog.AbilityWarning(logger, key, problem.Message);
        }
    }

    if (errors > 0)
    {
        ServerLog.AbilitiesInvalid(logger, errors);
    }
}

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
