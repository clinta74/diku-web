using Muwbta.Domain.Spawning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Muwbta.Persistence.Configurations;

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
        // Nullable on purpose: null is "follow the template", which is a third answer rather than
        // a missing one (PLAN.md §4.8).
        builder.Property(e => e.Wanders).HasColumnName("wanders");

        // Nullable for the same reason: null is "let the zone decide" (PLAN.md §4.7).
        builder.Property(e => e.FightsAtLevel).HasColumnName("fights_at_level");

        builder.HasIndex(e => e.ZoneKey).HasDatabaseName("ix_spawners_zone_key");
    }
}
