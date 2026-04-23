using Homestead.WebApi.Domain.Finances;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homestead.WebApi.Persistence.Configurations;

public class AccountTransactionSplitConfiguration : IEntityTypeConfiguration<AccountTransactionSplit>
{
    public void Configure(EntityTypeBuilder<AccountTransactionSplit> builder)
    {
        builder.ToTable("account_transaction_splits", "finances");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Quantity).HasColumnType("numeric");
        builder.Property(s => s.Value).HasColumnType("numeric");

        builder.Property(s => s.Memo).HasMaxLength(1024);

        builder.Property(s => s.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(s => s.UpdatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasOne(s => s.Transaction)
            .WithMany(t => t.Splits)
            .HasForeignKey(s => s.TransactionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.Account)
            .WithMany()
            .HasForeignKey(s => s.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Commodity)
            .WithMany()
            .HasForeignKey(s => s.CommodityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.CreatedByUser)
            .WithMany()
            .HasForeignKey(s => s.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(s => s.UpdatedByUser)
            .WithMany()
            .HasForeignKey(s => s.UpdatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(s => s.TransactionId);
        builder.HasIndex(s => new { s.AccountId, s.CommodityId });
    }
}
