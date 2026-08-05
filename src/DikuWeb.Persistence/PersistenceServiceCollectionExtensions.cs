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

        services.AddDbContext<DikuWebDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsAssembly(typeof(DikuWebDbContext).Assembly.GetName().Name)));

        // Register factory for spawning systems and other services that need to create their own DbContext.
        // Note: Disabled in Phase 4.1 due to EF Core 10 lifetime validation issues with scoped DbContextOptions.
        // Will re-enable when Phase 3 repositories are implemented.
        // services.AddDbContextFactory<DikuWebDbContext>(options =>
        //     options.UseNpgsql(
        //         connectionString,
        //         npgsql => npgsql.MigrationsAssembly(typeof(DikuWebDbContext).Assembly.GetName().Name)));

        return services;
    }
}
