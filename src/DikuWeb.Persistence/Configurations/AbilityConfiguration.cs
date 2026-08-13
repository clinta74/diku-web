using DikuWeb.Domain.Abilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DikuWeb.Persistence.Configurations;

internal sealed class AbilityConfiguration : IEntityTypeConfiguration<Ability>
{
    public void Configure(EntityTypeBuilder<Ability> builder)
    {
        builder.ToTable("abilities");

        builder.HasKey(e => e.Key);
        builder.Property(e => e.Key).HasColumnName("key").ValueGeneratedNever();

        builder.Property(e => e.Path).HasColumnName("path");
        builder.Property(e => e.UnlockLevel).HasColumnName("unlock_level");
        builder.Property(e => e.Name).HasColumnName("name");
        builder.Property(e => e.Description).HasColumnName("description");
        builder.Property(e => e.CostType).HasColumnName("cost_type");
        builder.Property(e => e.CostValue).HasColumnName("cost_value");
        builder.Property(e => e.CooldownPulses).HasColumnName("cooldown_pulses");
        builder.Property(e => e.CastTimePulses).HasColumnName("cast_time_pulses");
        builder.Property(e => e.TargetingType).HasColumnName("targeting_type");
        builder.Property(e => e.EffectKey).HasColumnName("effect_key");
        builder.Property(e => e.EffectParams).HasColumnName("effect_params").HasColumnType("jsonb");

        builder.HasIndex(e => e.TargetingType).HasDatabaseName("ix_abilities_targeting_type");

        // Every ability lookup a character makes is "what does my Path know by my level", so this
        // is the one access pattern worth an index rather than a scan of the whole table.
        builder.HasIndex(e => new { e.Path, e.UnlockLevel })
            .HasDatabaseName("ix_abilities_path_unlock_level");
    }
}
