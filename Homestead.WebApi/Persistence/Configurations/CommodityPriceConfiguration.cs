using Homestead.WebApi.Domain.Finances;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homestead.WebApi.Persistence.Configurations;

public class CommodityPriceConfiguration : IEntityTypeConfiguration<CommodityPrice>
{
    private const string RatePositiveSql = "\"Rate\" > 0";
    private const string NotSelfPriceSql = "\"CommodityId\" <> \"QuoteCurrencyId\"";

    public void Configure(EntityTypeBuilder<CommodityPrice> builder)
    {
        builder.ToTable("commodity_prices", "finances", table =>
        {
            table.HasCheckConstraint("CK_commodity_prices_rate_positive", RatePositiveSql);
            table.HasCheckConstraint("CK_commodity_prices_self_price", NotSelfPriceSql);
        });

        builder.HasKey(p => p.Id);

        builder.Property(p => p.PriceAt).IsRequired();

        builder.Property(p => p.Rate).HasColumnType("numeric");

        builder.Property(p => p.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(p => p.UpdatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasOne(p => p.Commodity)
            .WithMany()
            .HasForeignKey(p => p.CommodityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.QuoteCurrency)
            .WithMany()
            .HasForeignKey(p => p.QuoteCurrencyId)
            .OnDelete(DeleteBehavior.Restrict);

        // Transaction-linked prices cascade with their source transaction.
        builder.HasOne(p => p.CreatedByTransaction)
            .WithMany()
            .HasForeignKey(p => p.CreatedByTransactionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.CreatedByUser)
            .WithMany()
            .HasForeignKey(p => p.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(p => p.UpdatedByUser)
            .WithMany()
            .HasForeignKey(p => p.UpdatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(p => new { p.CommodityId, p.QuoteCurrencyId, p.PriceAt })
            .IsUnique()
            .IsDescending(false, false, true);
    }
}
