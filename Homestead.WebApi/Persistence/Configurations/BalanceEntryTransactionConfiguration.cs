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

        builder.Property(b => b.Note)
            .HasMaxLength(1024);

        builder.HasOne(b => b.Account)
            .WithMany()
            .HasForeignKey(b => b.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.AssertedCommodity)
            .WithMany()
            .HasForeignKey(b => b.AssertedCommodityId)
            .OnDelete(DeleteBehavior.Restrict);

        // One balance assertion per (account, commodity, date). Filter scopes the
        // uniqueness to balance-entry rows so it doesn't apply to the rest of TPH.
        builder.HasIndex(b => new { b.AccountId, b.AssertedCommodityId, b.TxnDate })
            .IsUnique()
            .HasFilter($"\"Type\" = '{nameof(TransactionType.BalanceEntry)}'");

        builder.HasIndex(b => new { b.AccountId, b.TxnDate })
            .HasFilter($"\"Type\" = '{nameof(TransactionType.BalanceEntry)}'");
    }
}
