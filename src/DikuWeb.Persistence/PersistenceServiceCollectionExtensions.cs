using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace DikuWeb.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers the database context. Migrations are applied explicitly on deploy,
    /// never by EnsureCreated (PLAN.md §6).
    /// </summary>
    public static IServiceCollection AddDikuWebPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // Create and configure the Npgsql data source
        var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(connectionString);
        // Enable dynamic JSON serialization for Dictionary<string, object> types (used in mob templates, etc)
        dataSourceBuilder.EnableDynamicJson();
        var dataSource = dataSourceBuilder.Build();

        // Pooled factory for all database access, including HTTP-scoped and background services.
        // Singleton repositories and the game loop use this to create DbContexts on demand.
        services.AddPooledDbContextFactory<DikuWebDbContext>(options =>
            options.UseNpgsql(
                dataSource,
                builder => builder.MigrationsAssembly(typeof(DikuWebDbContext).Assembly.GetName().Name)));

        return services;
    }
}
