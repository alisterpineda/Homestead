using Homestead.WebApi.Domain.Finances;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homestead.WebApi.Persistence.Configurations;

public class CommodityPriceConfiguration : IEntityTypeConfiguration<CommodityPrice>
{
    public void Configure(EntityTypeBuilder<CommodityPrice> builder)
    {
        builder.ToTable("commodity_prices", "finances");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.PriceDate).IsRequired();

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

        builder.HasOne(p => p.CreatedByUser)
            .WithMany()
            .HasForeignKey(p => p.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(p => p.UpdatedByUser)
            .WithMany()
            .HasForeignKey(p => p.UpdatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(p => new { p.CommodityId, p.QuoteCurrencyId, p.PriceDate })
            .IsUnique()
            .IsDescending(false, false, true);
    }
}
