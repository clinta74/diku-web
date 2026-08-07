using DikuWeb.Domain.Inhabitants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DikuWeb.Persistence.Configurations;

internal sealed class MobConfiguration : IEntityTypeConfiguration<Mob>
{
    public void Configure(EntityTypeBuilder<Mob> builder)
    {
        builder.ToTable("mobs");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.TemplateKey).HasColumnName("template_key");
        builder.Property(e => e.SpawnerId).HasColumnName("spawner_id");
        builder.Property(e => e.TemplateName).HasColumnName("template_name");
        builder.Property(e => e.Level).HasColumnName("level");
        builder.Property(e => e.RoomKey).HasColumnName("room_key");

        builder.OwnsOne(e => e.Vitals, v =>
        {
            v.Property(x => x.Health).HasColumnName("vitals_health");
            v.Property(x => x.HealthMax).HasColumnName("vitals_health_max");
            v.Property(x => x.Focus).HasColumnName("vitals_focus");
            v.Property(x => x.FocusMax).HasColumnName("vitals_focus_max");
            v.Property(x => x.Stamina).HasColumnName("vitals_stamina");
            v.Property(x => x.StaminaMax).HasColumnName("vitals_stamina_max");
        });

        builder.Property(e => e.ResolvedStats).HasColumnName("resolved_stats").HasColumnType("jsonb");
        builder.Property(e => e.SpawnMultipliers).HasColumnName("spawn_multipliers").HasColumnType("jsonb");
        builder.Property(e => e.ResolvedXp).HasColumnName("resolved_xp");
        builder.Property(e => e.ResolvedGold).HasColumnName("resolved_gold");
        builder.Property(e => e.CombatState).HasColumnName("combat_state");
        builder.Property(e => e.CurrentTarget).HasColumnName("current_target");
        builder.Property(e => e.State).HasColumnName("state").HasColumnType("jsonb");

        builder.HasIndex(e => e.RoomKey).HasDatabaseName("ix_mobs_room_key");
    }
}
