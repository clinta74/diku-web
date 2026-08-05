using DikuWeb.Domain.Accounts;
using DikuWeb.Domain.Characters;
using DikuWeb.Domain.Worlds;
using Microsoft.EntityFrameworkCore;

namespace DikuWeb.Persistence;

/// <summary>
/// PLAN.md §6: Postgres is the only source of truth. There are no content files.
/// </summary>
public sealed class DikuWebDbContext(DbContextOptions<DikuWebDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<Character> Characters => Set<Character>();

    public DbSet<World> Worlds => Set<World>();

    public DbSet<Zone> Zones => Set<Zone>();

    public DbSet<Room> Rooms => Set<Room>();

    public DbSet<RoomExit> RoomExits => Set<RoomExit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // Case-insensitive text for names and emails, so Kael and kael cannot both register.
        modelBuilder.HasPostgresExtension("citext");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DikuWebDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
