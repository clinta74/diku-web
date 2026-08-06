using DikuWeb.Domain.Quests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DikuWeb.Persistence.Configurations;

public sealed class QuestConfiguration : IEntityTypeConfiguration<Quest>
{
    public void Configure(EntityTypeBuilder<Quest> builder)
    {
        builder.ToTable("quests");

        builder.HasKey(q => q.Key);

        builder.Property(q => q.Key)
            .HasColumnName("key")
            .IsRequired();

        builder.Property(q => q.ZoneKey)
            .HasColumnName("zone_key")
            .IsRequired();

        builder.Property(q => q.Name)
            .HasColumnName("name")
            .IsRequired();

        builder.Property(q => q.Summary)
            .HasColumnName("summary");

        builder.Property(q => q.Description)
            .HasColumnName("description");

        builder.Property(q => q.GiverMobKey)
            .HasColumnName("giver_mob_key")
            .IsRequired();

        builder.Property(q => q.TurninMobKey)
            .HasColumnName("turnin_mob_key")
            .IsRequired();

        builder.Property(q => q.RequiredItemKey)
            .HasColumnName("required_item_key")
            .IsRequired();

        builder.Property(q => q.RequiredCount)
            .HasColumnName("required_count");

        builder.Property(q => q.RewardXp)
            .HasColumnName("reward_xp");

        builder.Property(q => q.RewardGold)
            .HasColumnName("reward_gold");

        builder.Property(q => q.RewardItemKey)
            .HasColumnName("reward_item_key");

        builder.Property(q => q.RewardItemCount)
            .HasColumnName("reward_item_count");

        builder.Property(q => q.PrerequisiteQuestKeys)
            .HasColumnName("prerequisite_quest_keys")
            .HasColumnType("text[]");

        builder.Property(q => q.IsRepeatable)
            .HasColumnName("is_repeatable");

        builder.Property(q => q.Dialogue)
            .HasColumnName("dialogue")
            .HasColumnType("jsonb");

        builder.Property(q => q.SortOrder)
            .HasColumnName("sort_order");

        // Indexes for common lookups
        builder.HasIndex(q => q.GiverMobKey)
            .HasDatabaseName("ix_quests_giver_mob_key");

        builder.HasIndex(q => q.TurninMobKey)
            .HasDatabaseName("ix_quests_turnin_mob_key");
    }
}
