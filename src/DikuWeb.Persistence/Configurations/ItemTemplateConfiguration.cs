using DikuWeb.Domain.Items;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DikuWeb.Persistence.Configurations;

internal sealed class ItemTemplateConfiguration : IEntityTypeConfiguration<ItemTemplate>
{
    public void Configure(EntityTypeBuilder<ItemTemplate> builder)
    {
        builder.HasKey(e => e.Key);
        builder.Property(e => e.Key).ValueGeneratedNever();

        builder.Property(e => e.BaseStats).HasColumnType("jsonb");
    }
}
