using DikuWeb.Domain.Abilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DikuWeb.Persistence.Configurations;

internal sealed class AbilityConfiguration : IEntityTypeConfiguration<Ability>
{
    public void Configure(EntityTypeBuilder<Ability> builder)
    {
        // Key is the natural identifier (e.g., "warden.slash")
        builder.HasKey(e => e.Key);
        builder.Property(e => e.Key).ValueGeneratedNever();

        // EffectParams is JSON
        builder.Property(e => e.EffectParams).HasColumnType("jsonb");

        // Indexes for common queries
        builder.HasIndex(e => e.TargetingType);
    }
}
