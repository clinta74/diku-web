using Muwbta.Domain.Characters;
using Muwbta.Domain.Worlds;
using Muwbta.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Muwbta.Persistence.Configurations;

internal sealed class RoomConfiguration : IEntityTypeConfiguration<Room>
{
    public void Configure(EntityTypeBuilder<Room> builder)
    {
        builder.ToTable("rooms");

        builder.HasKey(r => r.Key);
        builder.Property(r => r.Key)
            .HasColumnName("key")
            .HasConversion<RoomKeyConverter>()
            .HasMaxLength(RoomKey.MaxLength);

        builder.Property(r => r.ZoneKey).HasColumnName("zone_key").HasMaxLength(64).IsRequired();
        builder.HasIndex(r => r.ZoneKey).HasDatabaseName("ix_rooms_zone_key");

        builder.Property(r => r.Title).HasColumnName("title").HasMaxLength(128).IsRequired();
        builder.Property(r => r.Description).HasColumnName("description").IsRequired();

        // An open map, never promoted to columns (PLAN.md §4.10, §6). The moment one flag
        // becomes a column, adding the next one is a migration again.
        builder.Property(r => r.Flags)
            .HasColumnName("flags")
            .HasColumnType("jsonb")
            .HasConversion(new FlagSetConverter(), new FlagSetComparer())
            .IsRequired();

        // text[] rather than jsonb: the grid is genuinely an array of rows, and Postgres
        // arrays keep it readable in Adminer during world building.
        builder.Property(r => r.Grid)
            .HasColumnName("grid")
            .HasColumnType("text[]")
            .IsRequired();

        builder.Property(r => r.Legend)
            .HasColumnName("legend")
            .HasColumnType("jsonb")
            .HasConversion(new LegendConverter(), new LegendComparer())
            .IsRequired();

        // Builder canvas only (PLAN.md §7.2). Nullable: rooms without stored coordinates
        // get auto-laid-out from the exit graph the first time a builder opens the zone.
        builder.Property(r => r.EditorX).HasColumnName("editor_x");
        builder.Property(r => r.EditorY).HasColumnName("editor_y");

        builder.HasMany(r => r.Exits)
            .WithOne(e => e.FromRoom)
            .HasForeignKey(e => e.FromRoomKey)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class RoomExitConfiguration : IEntityTypeConfiguration<RoomExit>
{
    public void Configure(EntityTypeBuilder<RoomExit> builder)
    {
        builder.ToTable("room_exits");

        builder.HasKey(e => new { e.FromRoomKey, e.Direction });

        builder.Property(e => e.FromRoomKey)
            .HasColumnName("from_room_key")
            .HasConversion<RoomKeyConverter>()
            .HasMaxLength(RoomKey.MaxLength);

        builder.Property(e => e.Direction)
            .HasColumnName("direction")
            .HasConversion<string>()
            .HasMaxLength(8);

        // Deliberately NOT a foreign key. A builder links an exit before creating its
        // destination, and live editing has no publish gate to defer that to, so a FK would
        // reject the save outright (PLAN.md §6, §7.4).
        builder.Property(e => e.ToRoomKey)
            .HasColumnName("to_room_key")
            .HasConversion<RoomKeyConverter>()
            .HasMaxLength(RoomKey.MaxLength)
            .IsRequired();

        builder.HasIndex(e => e.ToRoomKey).HasDatabaseName("ix_room_exits_to_room_key");

        // What gates this way, if anything (PLAN.md §4.15). All nullable and all plain strings,
        // never foreign keys: a builder authors the gate before the quest that opens it exists,
        // exactly as they link an exit before digging its destination.
        builder.Property(e => e.RequiredFlagKey)
            .HasColumnName("required_flag_key")
            .HasMaxLength(CharacterFlags.MaxLength);

        builder.Property(e => e.RequiredItemKey)
            .HasColumnName("required_item_key")
            .HasMaxLength(64);

        builder.Property(e => e.RefusalMessage)
            .HasColumnName("refusal_message")
            .HasMaxLength(256);
    }
}
