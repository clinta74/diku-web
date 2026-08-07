using DikuWeb.Domain.Inhabitants;
using DikuWeb.Domain.Items;
using DikuWeb.Domain.Spawning;
using DikuWeb.Engine;
using DikuWeb.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DikuWeb.Server.Infrastructure;

/// <summary>
/// Read-only access to templates from the EF Core database for spawning.
/// Registered in DI as singletons so they can be queried on the game loop thread
/// during spawner sweeps (which run async but do not await on the loop).
/// </summary>
internal sealed class EfMobTemplateRepository(IDbContextFactory<DikuWebDbContext> factory) : IMobTemplateRepository
{
    public async Task<MobTemplate?> GetByKeyAsync(string key, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);

        await using var context = await factory.CreateDbContextAsync(ct);
        return await context.MobTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Key == key, ct);
    }

    public async Task<IReadOnlyList<MobTemplate>> GetAllAsync(CancellationToken ct)
    {
        await using var context = await factory.CreateDbContextAsync(ct);
        return await context.MobTemplates.AsNoTracking().ToListAsync(ct);
    }
}

internal sealed class EfItemTemplateRepository(IDbContextFactory<DikuWebDbContext> factory) : IItemTemplateRepository
{
    public async Task<ItemTemplate?> GetByKeyAsync(string key, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);

        await using var context = await factory.CreateDbContextAsync(ct);
        return await context.ItemTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Key == key, ct);
    }

    public async Task<IReadOnlyList<ItemTemplate>> GetAllAsync(CancellationToken ct)
    {
        await using var context = await factory.CreateDbContextAsync(ct);
        return await context.ItemTemplates.AsNoTracking().ToListAsync(ct);
    }
}

internal sealed class EfSpawnerRepository(IDbContextFactory<DikuWebDbContext> factory) : ISpawnerRepository
{
    public async Task<IReadOnlyList<Spawner>> GetAllAsync(CancellationToken ct)
    {
        await using var context = await factory.CreateDbContextAsync(ct);
        return await context.Spawners.AsNoTracking().ToListAsync(ct);
    }
}
