using Muwbta.Domain.Quests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Muwbta.Persistence.Configurations;

public sealed class CharacterQuestConfiguration : IEntityTypeConfiguration<CharacterQuest>
{
    public void Configure(EntityTypeBuilder<CharacterQuest> builder)
    {
        builder.ToTable("character_quests");

        builder.HasKey(cq => new { cq.CharacterId, cq.QuestKey });

        builder.Property(cq => cq.CharacterId)
            .HasColumnName("character_id")
            .IsRequired();

        builder.Property(cq => cq.QuestKey)
            .HasColumnName("quest_key")
            .IsRequired();

        builder.Property(cq => cq.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .IsRequired();

        builder.Property(cq => cq.StartedAt)
            .HasColumnName("started_at")
            .IsRequired();

        builder.Property(cq => cq.CompletedAt)
            .HasColumnName("completed_at");

        builder.Property(cq => cq.TimesCompleted)
            .HasColumnName("times_completed");

        // Indexes for common lookups
        builder.HasIndex(cq => cq.CharacterId)
            .HasDatabaseName("ix_character_quests_character_id");

        builder.HasIndex(cq => new { cq.CharacterId, cq.Status })
            .HasDatabaseName("ix_character_quests_character_id_status")
            .HasFilter("status = 'Active'");
    }
}
