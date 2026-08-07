using DikuWeb.Domain.Abilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DikuWeb.Persistence.Configurations;

internal sealed class AbilityConfiguration : IEntityTypeConfiguration<Ability>
{
    public void Configure(EntityTypeBuilder<Ability> builder)
    {
        builder.HasKey(e => e.Key);
        builder.Property(e => e.Key).HasColumnName("key").ValueGeneratedNever();

        builder.Property(e => e.Name).HasColumnName("name");
        builder.Property(e => e.Description).HasColumnName("description");
        builder.Property(e => e.CostType).HasColumnName("cost_type");
        builder.Property(e => e.CostValue).HasColumnName("cost_value");
        builder.Property(e => e.CooldownPulses).HasColumnName("cooldown_pulses");
        builder.Property(e => e.CastTimePulses).HasColumnName("cast_time_pulses");
        builder.Property(e => e.TargetingType).HasColumnName("targeting_type");
        builder.Property(e => e.EffectKey).HasColumnName("effect_key");
        builder.Property(e => e.EffectParams).HasColumnName("effect_params").HasColumnType("jsonb");

        builder.HasIndex(e => e.TargetingType);
    }
}
