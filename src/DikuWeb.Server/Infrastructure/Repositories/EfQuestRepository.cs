using DikuWeb.Domain.Quests;
using DikuWeb.Engine;
using DikuWeb.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DikuWeb.Server.Infrastructure.Repositories;

public sealed class EfQuestRepository(IDbContextFactory<DikuWebDbContext> factory) : IQuestRepository
{
    public async Task<Quest?> GetByKeyAsync(string key, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);

        using var db = await factory.CreateDbContextAsync(ct);
        return await db.Quests
            .AsNoTracking()
            .FirstOrDefaultAsync(q => q.Key == key, cancellationToken: ct);
    }

    public async Task<IReadOnlyList<Quest>> GetAllAsync(CancellationToken ct)
    {
        using var db = await factory.CreateDbContextAsync(ct);
        return await db.Quests
            .AsNoTracking()
            .ToListAsync(cancellationToken: ct);
    }
}
