using DikuWeb.Domain.Inhabitants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DikuWeb.Persistence.Configurations;

internal sealed class MobTemplateConfiguration : IEntityTypeConfiguration<MobTemplate>
{
    public void Configure(EntityTypeBuilder<MobTemplate> builder)
    {
        builder.HasKey(e => e.Key);
        builder.Property(e => e.Key).ValueGeneratedNever();

        builder.Property(e => e.BaseStats).HasColumnType("jsonb");
        builder.Property(e => e.Behavior).HasColumnType("jsonb");
        builder.Property(e => e.Loot).HasColumnType("jsonb");
    }
}
