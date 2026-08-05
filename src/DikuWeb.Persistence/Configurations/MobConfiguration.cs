using DikuWeb.Domain.Inhabitants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DikuWeb.Persistence.Configurations;

internal sealed class MobConfiguration : IEntityTypeConfiguration<Mob>
{
    public void Configure(EntityTypeBuilder<Mob> builder)
    {
        builder.HasKey(e => e.Id);

        builder.OwnsOne(e => e.Vitals);

        builder.Property(e => e.ResolvedStats).HasColumnType("jsonb");
        builder.Property(e => e.SpawnMultipliers).HasColumnType("jsonb");
        builder.Property(e => e.State).HasColumnType("jsonb");

        builder.HasIndex(e => e.RoomKey);
    }
}
