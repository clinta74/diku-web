using DikuWeb.Persistence;
using DikuWeb.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace DikuWeb.Server.Tests.Infrastructure;

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
        // DikuWebAppFactory; this is the headroom behind it, and costs nothing in a container
        // that lives for one test run.
        .WithCommand("-c", "max_connections=300")
        .Build();

    private Npgsql.NpgsqlDataSource? _dataSource;

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Migrations, never EnsureCreated (PLAN.md §6). This also means the test run
        // fails if a migration is broken, which is the point.
        var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(ConnectionString);
        dataSourceBuilder.EnableDynamicJson();
        _dataSource = dataSourceBuilder.Build();

        var options = new DbContextOptionsBuilder<DikuWebDbContext>()
            .UseNpgsql(_dataSource)
            .Options;

        await using var db = new DikuWebDbContext(options);
        await db.Database.MigrateAsync();

        // The server only seeds in Development, and the test factory runs as "Testing", so
        // seed here. Without a world the game loop would load zero rooms and every entry
        // would land in a room that does not exist.
        await StarterWorldSeeder.SeedAsync(db);
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    public DikuWebDbContext CreateDbContext()
    {
        ArgumentNullException.ThrowIfNull(_dataSource);

        var options = new DbContextOptionsBuilder<DikuWebDbContext>()
            .UseNpgsql(_dataSource)
            .Options;

        return new DikuWebDbContext(options);
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
