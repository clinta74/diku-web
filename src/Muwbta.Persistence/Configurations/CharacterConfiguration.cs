using Muwbta.Domain.Characters;
using Muwbta.Domain.Worlds;
using Muwbta.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Muwbta.Persistence.Configurations;

internal sealed class CharacterConfiguration : IEntityTypeConfiguration<Character>
{
    public void Configure(EntityTypeBuilder<Character> builder)
    {
        builder.ToTable("characters");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("uuidv7()");

        builder.Property(c => c.AccountId).HasColumnName("account_id").IsRequired();
        builder.HasIndex(c => c.AccountId).HasDatabaseName("ix_characters_account_id");

        builder.Property(c => c.Name)
            .HasColumnName("name")
            .HasColumnType("citext")
            .HasMaxLength(24)
            .IsRequired();
        builder.HasIndex(c => c.Name).IsUnique().HasDatabaseName("ix_characters_name");

        builder.Property(c => c.Path)
            .HasColumnName("path")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(c => c.Level).HasColumnName("level").IsRequired();
        builder.Property(c => c.Xp).HasColumnName("xp").IsRequired();

        // PLAN.md §6: jsonb for stat blocks, because they change shape constantly during
        // design and a migration per stat tweak would be miserable.
        builder.OwnsOne(c => c.Attributes, b => b.ToJson("attributes"));
        builder.OwnsOne(c => c.Vitals, b => b.ToJson("vitals"));

        // Deliberately a plain string, not a foreign key: live editing means a builder can
        // delete a room out from under a saved character, and that has to degrade
        // gracefully rather than fail the write (PLAN.md §7.4).
        builder.Property(c => c.RoomKey)
            .HasColumnName("room_key")
            .HasConversion<RoomKeyConverter>()
            .HasMaxLength(RoomKey.MaxLength)
            .IsRequired();

        // Respawn point, also a plain string for the same reason (PLAN.md §4.12)
        builder.Property(c => c.RespawnRoomKey)
            .HasColumnName("respawn_room_key")
            .HasConversion<RoomKeyConverter>()
            .HasMaxLength(RoomKey.MaxLength);

        builder.Property(c => c.Gold).HasColumnName("gold").IsRequired();

        // Capabilities earned, usually from a quest (PLAN.md §4.15). text[] rather than jsonb
        // because a character flag is held or it is not - there is no inheritance chain here to
        // need an absent-versus-false distinction, so the set is the whole truth.
        builder.Property(c => c.Flags)
            .HasColumnName("flags")
            .HasColumnType("text[]")
            .IsRequired();
        builder.Property(c => c.RestState).HasColumnName("rest_state").HasConversion<int>().IsRequired();
        builder.Property(c => c.CombatState).HasColumnName("combat_state").HasConversion<int>().IsRequired();
        builder.Property(c => c.CurrentTarget).HasColumnName("current_target").HasMaxLength(64);

        builder.Property(c => c.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(c => c.LastPlayedAt).HasColumnName("last_played_at");
        builder.Property(c => c.PlaytimeSeconds).HasColumnName("playtime_seconds").IsRequired();
        builder.Property(c => c.DeletedAt).HasColumnName("deleted_at");
    }
}
