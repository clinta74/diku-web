using Muwbta.Persistence;
using Muwbta.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Muwbta.Server.Tests.Infrastructure;

/// <summary>
/// A real PostgreSQL 18 in a container, migrated once and shared by every test in the
/// collection. Pinned to the same image tag as docker-compose.yml so dev and test cannot
/// drift apart (PLAN.md §6) - an in-memory provider would not exercise citext, jsonb,
/// or uuidv7() at all.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18")
        .WithDatabase("dikuweb_test")
        .WithUsername("dikuweb_test")
        .WithPassword("dikuweb_test")

        // Postgres defaults to 100, which a run that boots a host per test can brush against
        // while a finished test's pool is still winding down. The per-host pool is capped in
        // MuwbtaAppFactory; this is the headroom behind it, and costs nothing in a container
        // that lives for one test run.
        .WithCommand("-c", "max_connections=300")
        .Build();

    private Npgsql.NpgsqlDataSource? _dataSource;
    private MuwbtaAppFactory? _app;

    public string ConnectionString => _container.GetConnectionString();

    /// <summary>
    /// One server host shared by the whole collection.
    /// </summary>
    /// <remarks>
    /// Booting a host per test cost about a second each and dominated the run - roughly ninety
    /// seconds of the suite was startup rather than assertions. Sharing is safe because the
    /// tests were already written for a shared database: accounts, characters, worlds, and
    /// zones all come from <see cref="BuilderClient.UniqueName"/>, so nothing collides over a
    /// key. Per-account state such as the dig throttle and the session cap is likewise isolated
    /// by the unique account each test registers.
    ///
    /// What is genuinely shared is the in-memory world. Characters entered by earlier tests
    /// stay put for the link-dead grace window, so the starting room is not empty and an
    /// assertion that a player is *alone* there cannot hold. Assert that the viewer is present
    /// instead - that is what such a test means, and it holds either way.
    ///
    /// Do not dispose this from a test. Tests that need a differently configured host - the
    /// readiness checks, which point at a database that is deliberately unreachable - still
    /// construct their own.
    /// </remarks>
    public MuwbtaAppFactory App =>
        _app ?? throw new InvalidOperationException("The fixture has not been initialized yet.");

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Migrations, never EnsureCreated (PLAN.md §6). This also means the test run
        // fails if a migration is broken, which is the point.
        var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(ConnectionString);
        dataSourceBuilder.EnableDynamicJson();
        _dataSource = dataSourceBuilder.Build();

        var options = new DbContextOptionsBuilder<MuwbtaDbContext>()
            .UseNpgsql(_dataSource)
            .Options;

        await using var db = new MuwbtaDbContext(options);
        await db.Database.MigrateAsync();

        // The server only seeds in Development, and the test factory runs as "Testing", so
        // seed here. Without a world the game loop would load zero rooms and every entry
        // would land in a room that does not exist.
        await StarterWorldSeeder.SeedAsync(db);

        // And the starter configuration (§4.16), for the same reason: a development database has
        // one, so a test database without one is a different shape from the thing under test.
        await StarterWorldSeeder.ReconcileStarterConfigurationAsync(db);

        // Built after seeding: the host loads the world into memory at startup, so a host
        // created before the starter world exists would come up with zero rooms.
        _app = new MuwbtaAppFactory(ConnectionString);

        // WebApplicationFactory builds its host lazily on first use. Doing it here means the
        // one-off startup cost lands in fixture setup rather than being blamed on whichever
        // test happened to run first.
        _ = _app.Services;
    }

    public async Task DisposeAsync()
    {
        _app?.Dispose();
        await _container.DisposeAsync();
    }

    public MuwbtaDbContext CreateDbContext()
    {
        ArgumentNullException.ThrowIfNull(_dataSource);

        var options = new DbContextOptionsBuilder<MuwbtaDbContext>()
            .UseNpgsql(_dataSource)
            .Options;

        return new MuwbtaDbContext(options);
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
