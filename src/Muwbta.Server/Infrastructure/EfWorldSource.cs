using Muwbta.Engine;
using Muwbta.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Muwbta.Server.Infrastructure;

/// <summary>
/// Loads the world into memory once at boot. The Engine cannot reference EF Core, so this
/// adapter lives in the Server (PLAN.md §2.2).
/// </summary>
public sealed class EfWorldSource(IDbContextFactory<MuwbtaDbContext> factory) : IWorldSource
{
    public async Task<WorldData> LoadAsync(CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        var worlds = await db.Worlds.AsNoTracking().ToListAsync(cancellationToken);
        var zones = await db.Zones.AsNoTracking().ToListAsync(cancellationToken);

        // Exits are included because every movement reads them; a lazy load would be a
        // database call from the game loop thread, which is exactly what §2.1 forbids.
        var rooms = await db.Rooms
            .AsNoTracking()
            .Include(r => r.Exits)
            .ToListAsync(cancellationToken);

        return new WorldData(worlds, zones, rooms);
    }
}
