using DikuWeb.Domain.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DikuWeb.Persistence.Configurations;

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("accounts");

        builder.HasKey(a => a.Id);

        // The app supplies Guid.CreateVersion7() on construction, so this default is a
        // safety net for rows inserted by hand or by a script rather than the normal path.
        builder.Property(a => a.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("uuidv7()");

        builder.Property(a => a.Email)
            .HasColumnName("email")
            .HasColumnType("citext")
            .HasMaxLength(320)
            .IsRequired();
        builder.HasIndex(a => a.Email).IsUnique().HasDatabaseName("ix_accounts_email");

        builder.Property(a => a.Username)
            .HasColumnName("username")
            .HasColumnType("citext")
            .HasMaxLength(32)
            .IsRequired();
        builder.HasIndex(a => a.Username).IsUnique().HasDatabaseName("ix_accounts_username");

        builder.Property(a => a.PasswordHash)
            .HasColumnName("password_hash")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(a => a.PasswordChangedAt).HasColumnName("password_changed_at");

        // Stored as text rather than an int so the column is readable in Adminer and
        // so inserting a new role in the middle of the enum cannot silently reassign rows.
        builder.Property(a => a.Role)
            .HasColumnName("role")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(a => a.LastLoginAt).HasColumnName("last_login_at");
        builder.Property(a => a.IsBanned).HasColumnName("is_banned").IsRequired();
        builder.Property(a => a.BanReason).HasColumnName("ban_reason").HasMaxLength(512);
        builder.Property(a => a.MutedUntil).HasColumnName("muted_until");

        builder.HasMany(a => a.Characters)
            .WithOne(c => c.Account)
            .HasForeignKey(c => c.AccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
