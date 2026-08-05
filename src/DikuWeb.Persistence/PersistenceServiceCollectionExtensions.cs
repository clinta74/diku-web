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

        return services;
    }
}
