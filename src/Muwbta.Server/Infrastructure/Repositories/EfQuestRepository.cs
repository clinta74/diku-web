using Muwbta.Domain.Quests;
using Muwbta.Engine;
using Muwbta.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Muwbta.Server.Infrastructure.Repositories;

public sealed class EfQuestRepository(IDbContextFactory<MuwbtaDbContext> factory) : IQuestRepository
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
