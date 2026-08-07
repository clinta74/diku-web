using DikuWeb.Domain.Spawning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DikuWeb.Persistence.Configurations;

internal sealed class SpawnerConfiguration : IEntityTypeConfiguration<Spawner>
{
    public void Configure(EntityTypeBuilder<Spawner> builder)
    {
        builder.ToTable("spawners");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.ZoneKey).HasColumnName("zone_key");
        builder.Property(e => e.TemplateKey).HasColumnName("template_key");
        builder.Property(e => e.TemplateKind).HasColumnName("template_kind");
        builder.Property(e => e.RoomKeys).HasColumnName("room_keys").HasColumnType("text[]");
        builder.Property(e => e.TargetCount).HasColumnName("target_count");
        builder.Property(e => e.RespawnSeconds).HasColumnName("respawn_seconds");
        builder.Property(e => e.Sentinel).HasColumnName("sentinel");

        builder.HasIndex(e => e.ZoneKey).HasDatabaseName("ix_spawners_zone_key");
    }
}
