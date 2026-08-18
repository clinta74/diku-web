using DikuWeb.Domain.Characters;
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

        // Deliberately nullable: a quest may be pure dialogue with no fetch objective, which the
        // domain property (string?) has always allowed. Marking it required here made such a
        // quest unsaveable.
        builder.Property(q => q.RequiredItemKey)
            .HasColumnName("required_item_key");

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

        // The character flag this quest grants on completion (PLAN.md §4.15). Nullable, and a
        // plain string for the same reason the mob and item keys are.
        builder.Property(q => q.RewardFlagKey)
            .HasColumnName("reward_flag_key")
            .HasMaxLength(CharacterFlags.MaxLength);

        builder.Property(q => q.PrerequisiteQuestKeys)
            .HasColumnName("prerequisite_quest_keys")
            .HasColumnType("text[]");

        builder.Property(q => q.IsRepeatable)
            .HasColumnName("is_repeatable");

        // The Paths this quest is for, as a jsonb array of names - the same shape and the same

        // argument as ItemTemplateConfiguration's: a short, unordered, read-only list only ever

        // wanted alongside its row.

        builder.Property(q => q.Paths)

            .HasColumnName("paths")

            .HasColumnType("jsonb")

            .HasConversion(

                v => System.Text.Json.JsonSerializer.Serialize(

                    v.Select(x => x.ToString()), (System.Text.Json.JsonSerializerOptions?)null),

                v => System.Text.Json.JsonSerializer

                        .Deserialize<List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null)!

                        .Select(Enum.Parse<DikuWeb.Domain.Characters.CharacterPath>)

                        .ToList(),

                new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<DikuWeb.Domain.Characters.CharacterPath>>(

                    (a, b) => a!.SequenceEqual(b!),

                    v => v.Aggregate(0, (acc, x) => HashCode.Combine(acc, x.GetHashCode())),

                    v => v.ToList()));


        builder.Property(q => q.AutoStart)
            .HasColumnName("auto_start");

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
