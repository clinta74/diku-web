using Muwbta.Domain.Quests;
using Muwbta.Engine;
using Muwbta.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Muwbta.Server.Infrastructure.Repositories;

public sealed class EfCharacterQuestRepository(IDbContextFactory<MuwbtaDbContext> factory) : ICharacterQuestRepository
{
    public async Task<IReadOnlyList<CharacterQuest>> GetForCharacterAsync(Guid characterId, CancellationToken ct)
    {
        using var db = await factory.CreateDbContextAsync(ct);
        return await db.CharacterQuests
            .AsNoTracking()
            .Where(cq => cq.CharacterId == characterId)
            .ToListAsync(cancellationToken: ct);
    }

    public async Task<CharacterQuest?> GetByKeyAsync(Guid characterId, string questKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(questKey);

        using var db = await factory.CreateDbContextAsync(ct);
        return await db.CharacterQuests
            .AsNoTracking()
            .FirstOrDefaultAsync(cq => cq.CharacterId == characterId && cq.QuestKey == questKey, cancellationToken: ct);
    }
}
