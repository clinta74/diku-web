using DikuWeb.Domain.Spawning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DikuWeb.Persistence.Configurations;

internal sealed class SpawnerConfiguration : IEntityTypeConfiguration<Spawner>
{
    public void Configure(EntityTypeBuilder<Spawner> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.RoomKeys).HasColumnType("text[]");

        builder.HasIndex(e => e.ZoneKey);
    }
}
