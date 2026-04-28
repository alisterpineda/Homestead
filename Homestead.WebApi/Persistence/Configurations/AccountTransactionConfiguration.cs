using Homestead.WebApi.Domain.Finances;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homestead.WebApi.Persistence.Configurations;

public class AccountTransactionConfiguration : IEntityTypeConfiguration<AccountTransaction>
{
    public void Configure(EntityTypeBuilder<AccountTransaction> builder)
    {
        builder.ToTable("account_transactions", "finances");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.OccurredAt).IsRequired();

        builder.Property(t => t.Description)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(t => t.Type)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.HasDiscriminator(t => t.Type)
            .HasValue<NormalTransaction>(TransactionType.Normal)
            .HasValue<OpeningBalanceTransaction>(TransactionType.OpeningBalance)
            .HasValue<BalanceEntryTransaction>(TransactionType.BalanceEntry)
            .HasValue<StockSplitTransaction>(TransactionType.StockSplit);

        builder.Property(t => t.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(t => t.UpdatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasOne(t => t.CreatedByUser)
            .WithMany()
            .HasForeignKey(t => t.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(t => t.UpdatedByUser)
            .WithMany()
            .HasForeignKey(t => t.UpdatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(t => t.RelatedTransaction)
            .WithMany()
            .HasForeignKey(t => t.RelatedTransactionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(t => t.OccurredAt);
        builder.HasIndex(t => t.RelatedTransactionId);
    }
}
