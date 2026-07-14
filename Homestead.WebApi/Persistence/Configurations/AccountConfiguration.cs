using Homestead.WebApi.Domain.Finances;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homestead.WebApi.Persistence.Configurations;

public class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("accounts", "finances");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Name)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(a => a.AccountType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(a => a.Kind)
            .HasConversion<string>()
            .HasMaxLength(24);

        builder.Property(a => a.Institution)
            .HasMaxLength(128);

        builder.Property(a => a.IsSystem).IsRequired();
        builder.Property(a => a.IsArchived).IsRequired();

        builder.Property(a => a.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(a => a.UpdatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasOne(a => a.ReportingCurrency)
            .WithMany()
            .HasForeignKey(a => a.ReportingCurrencyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.CreatedByUser)
            .WithMany()
            .HasForeignKey(a => a.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(a => a.UpdatedByUser)
            .WithMany()
            .HasForeignKey(a => a.UpdatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        // Self-referencing hierarchy for Income/Expense category accounts. Restrict so a category with
        // children can't be orphan-deleted through the FK — DeleteCategory reparents children first.
        builder.HasOne(a => a.ParentAccount)
            .WithMany(a => a.ChildAccounts)
            .HasForeignKey(a => a.ParentAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => a.Kind)
            .HasFilter("\"IsArchived\" = FALSE");

        // Sibling-unique category names, scoped to (parent, type). Filtered to categories so balance-sheet
        // accounts keep today's no-uniqueness behaviour. AreNullsDistinct(false) → PG15+ NULLS NOT DISTINCT,
        // so two top-level ParentAccountId=NULL siblings still collide (a NULL parent = "root", not "unique").
        builder.HasIndex(a => new { a.ParentAccountId, a.AccountType, a.Name })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasFilter("\"AccountType\" IN ('Income', 'Expense')");
    }
}
