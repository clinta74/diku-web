using Muwbta.Domain.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Muwbta.Persistence.Configurations;

internal sealed class AdminAuditConfiguration : IEntityTypeConfiguration<AdminAudit>
{
    public void Configure(EntityTypeBuilder<AdminAudit> builder)
    {
        builder.ToTable("admin_audit");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("uuidv7()")
            .ValueGeneratedOnAdd();

        // Neither account is a foreign key: deleting an account must not erase the record of
        // what was done to it, or of what it did.
        builder.Property(a => a.ActorAccountId).HasColumnName("actor_account_id");
        builder.Property(a => a.TargetAccountId).HasColumnName("target_account_id").IsRequired();

        builder.Property(a => a.Action)
            .HasColumnName("action")
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsRequired();

        builder.Property(a => a.Before).HasColumnName("before").HasMaxLength(64);
        builder.Property(a => a.After).HasColumnName("after").HasMaxLength(64);
        builder.Property(a => a.Reason).HasColumnName("reason").HasMaxLength(512);
        builder.Property(a => a.At).HasColumnName("at").IsRequired();

        // "What has been done to this account, newest first" - the moderation query.
        builder.HasIndex(a => new { a.TargetAccountId, a.At })
            .HasDatabaseName("ix_admin_audit_target")
            .IsDescending(false, true);
    }
}
