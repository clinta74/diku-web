using DikuWeb.Domain.Items;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DikuWeb.Persistence.Configurations;

internal sealed class ItemTemplateConfiguration : IEntityTypeConfiguration<ItemTemplate>
{
    public void Configure(EntityTypeBuilder<ItemTemplate> builder)
    {
        builder.HasKey(e => e.Key);
        builder.Property(e => e.Key).HasColumnName("key").ValueGeneratedNever();

        builder.Property(e => e.Name).HasColumnName("name");
        builder.Property(e => e.Description).HasColumnName("description");
        builder.Property(e => e.Icon).HasColumnName("icon");
        builder.Property(e => e.Slot).HasColumnName("slot");
        builder.Property(e => e.Weight).HasColumnName("weight");
        builder.Property(e => e.BaseValue).HasColumnName("base_value");
        builder.Property(e => e.BaseStats).HasColumnName("base_stats").HasColumnType("jsonb");
    }
}
