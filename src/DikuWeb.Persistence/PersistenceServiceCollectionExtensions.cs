using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

        // Pooled factory for all database access, including HTTP-scoped and background services.
        // Singleton repositories and the game loop use this to create DbContexts on demand.
        services.AddPooledDbContextFactory<DikuWebDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsAssembly(typeof(DikuWebDbContext).Assembly.GetName().Name)));

        return services;
    }
}
