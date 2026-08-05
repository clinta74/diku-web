using DikuWeb.Domain.Building;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DikuWeb.Persistence.Configurations;

internal sealed class ContentAuditConfiguration : IEntityTypeConfiguration<ContentAudit>
{
    public void Configure(EntityTypeBuilder<ContentAudit> builder)
    {
        builder.ToTable("content_audit");

        builder.HasKey(a => a.Id);

        // UUIDv7 generated in .NET (PLAN.md §6). This is one of the append-heavy tables the
        // choice exists for: v4 keys would scatter inserts across the whole B-tree.
        builder.Property(a => a.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("uuidv7()")
            .ValueGeneratedOnAdd();

        // Not a foreign key: deleting an account must not delete the record of what it built.
        builder.Property(a => a.AccountId).HasColumnName("account_id");

        builder.Property(a => a.EntityKind).HasColumnName("entity_kind").HasMaxLength(16).IsRequired();
        builder.Property(a => a.EntityKey).HasColumnName("entity_key").HasMaxLength(160).IsRequired();

        builder.Property(a => a.Action)
            .HasColumnName("action")
            .HasConversion<string>()
            .HasMaxLength(8)
            .IsRequired();

        builder.Property(a => a.Before).HasColumnName("before").HasColumnType("jsonb");
        builder.Property(a => a.After).HasColumnName("after").HasColumnType("jsonb");
        builder.Property(a => a.At).HasColumnName("at").IsRequired();

        // The history query is always "this entity, newest first".
        builder.HasIndex(a => new { a.EntityKind, a.EntityKey, a.At })
            .HasDatabaseName("ix_content_audit_entity")
            .IsDescending(false, false, true);
    }
}
