using DikuWeb.Domain.Items;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DikuWeb.Persistence.Configurations;

internal sealed class ItemInstanceConfiguration : IEntityTypeConfiguration<ItemInstance>
{
    public void Configure(EntityTypeBuilder<ItemInstance> builder)
    {
        builder.HasKey(e => e.Id);

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_item_instance_location",
            "(num_nonnulls(owner_character_id, container_item_id, room_key) = 1)"));

        builder.Property(e => e.OwnerCharacterId).HasColumnName("owner_character_id");
        builder.Property(e => e.ContainerItemId).HasColumnName("container_item_id");
        builder.Property(e => e.RoomKey).HasColumnName("room_key");
        builder.Property(e => e.EquippedSlot).HasColumnName("equipped_slot");

        builder.Property(e => e.ResolvedStats).HasColumnType("jsonb").HasColumnName("resolved_stats");
        builder.Property(e => e.SpawnMultipliers).HasColumnType("jsonb").HasColumnName("spawn_multipliers");
        builder.Property(e => e.State).HasColumnType("jsonb");

        builder.HasIndex(e => e.OwnerCharacterId);
        builder.HasIndex(e => e.RoomKey);
    }
}
