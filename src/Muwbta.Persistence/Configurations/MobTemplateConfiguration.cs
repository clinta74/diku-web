using Muwbta.Domain.Inhabitants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Muwbta.Persistence.Configurations;

internal sealed class MobTemplateConfiguration : IEntityTypeConfiguration<MobTemplate>
{
    public void Configure(EntityTypeBuilder<MobTemplate> builder)
    {
        builder.ToTable("mob_templates");

        builder.HasKey(e => e.Key);
        builder.Property(e => e.Key).HasColumnName("key").ValueGeneratedNever();

        builder.Property(e => e.Name).HasColumnName("name");
        builder.Property(e => e.Description).HasColumnName("description");
        builder.Property(e => e.Icon).HasColumnName("icon");
        builder.Property(e => e.Level).HasColumnName("level");
        builder.Property(e => e.WanderIntervalPulses).HasColumnName("wander_interval_pulses");
        builder.Property(e => e.BaseStats).HasColumnName("base_stats").HasColumnType("jsonb");
        builder.Property(e => e.BaseXp).HasColumnName("base_xp");
        builder.Property(e => e.BaseGold).HasColumnName("base_gold");
        builder.Property(e => e.Behavior).HasColumnName("behavior").HasColumnType("jsonb");
        builder.Property(e => e.Loot).HasColumnName("loot").HasColumnType("jsonb");
        builder.Property(e => e.Attacks).HasColumnName("attacks").HasColumnType("jsonb");
    }
}
