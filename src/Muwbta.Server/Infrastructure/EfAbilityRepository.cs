using Muwbta.Domain.Abilities;
using Muwbta.Engine;
using Muwbta.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Muwbta.Server.Infrastructure;

/// <summary>
/// Read-only access to ability templates from EF Core database.
/// Registered as a singleton so abilities are available on the game loop thread.
/// </summary>
internal sealed class EfAbilityRepository(IDbContextFactory<MuwbtaDbContext> factory) : IAbilityRepository
{
    public async Task<Ability?> GetByKeyAsync(string key, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);

        await using var context = await factory.CreateDbContextAsync(ct);
        return await context.Abilities.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Key == key, ct);
    }

    public async Task<IReadOnlyList<Ability>> GetAllAsync(CancellationToken ct)
    {
        await using var context = await factory.CreateDbContextAsync(ct);
        return await context.Abilities.AsNoTracking().ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Ability>> GetByTargetingTypeAsync(
        TargetingType targetingType, CancellationToken ct)
    {
        await using var context = await factory.CreateDbContextAsync(ct);
        return await context.Abilities.AsNoTracking()
            .Where(a => a.TargetingType == targetingType)
            .ToListAsync(ct);
    }
}
