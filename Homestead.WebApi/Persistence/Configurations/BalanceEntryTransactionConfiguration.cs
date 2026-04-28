using Homestead.WebApi.Domain.Finances;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homestead.WebApi.Persistence.Configurations;

public class BalanceEntryTransactionConfiguration : IEntityTypeConfiguration<BalanceEntryTransaction>
{
    public void Configure(EntityTypeBuilder<BalanceEntryTransaction> builder)
    {
        builder.Property(b => b.AssertedQuantity)
            .HasColumnType("numeric");

        builder.HasOne(b => b.Account)
            .WithMany()
            .HasForeignKey(b => b.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.AssertedCommodity)
            .WithMany()
            .HasForeignKey(b => b.AssertedCommodityId)
            .OnDelete(DeleteBehavior.Restrict);

        // One balance assertion per (account, commodity, instant). Balance entries
        // are always emitted at 23:59:59 UTC on asOfDate so per-instant uniqueness
        // enforces per-day uniqueness. Filter scopes uniqueness to balance-entry rows
        // so it doesn't apply to the rest of TPH.
        builder.HasIndex(b => new { b.AccountId, b.AssertedCommodityId, b.OccurredAt })
            .IsUnique()
            .HasFilter($"\"Type\" = '{nameof(TransactionType.BalanceEntry)}'");

        builder.HasIndex(b => new { b.AccountId, b.OccurredAt })
            .HasFilter($"\"Type\" = '{nameof(TransactionType.BalanceEntry)}'");
    }
}
