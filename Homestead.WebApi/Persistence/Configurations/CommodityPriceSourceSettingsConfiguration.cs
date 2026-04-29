using Homestead.WebApi.Domain.Finances;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homestead.WebApi.Persistence.Configurations;

public class CommodityPriceSourceSettingsConfiguration : IEntityTypeConfiguration<CommodityPriceSourceSettings>
{
    // System enum values (Transaction, Manual) must never have a settings row.
    private const string SourceKindCheckSql =
        "\"Source\" NOT IN ('Transaction', 'Manual')";

    public void Configure(EntityTypeBuilder<CommodityPriceSourceSettings> builder)
    {
        builder.ToTable("commodity_price_source_settings", "finances", table =>
        {
            table.HasCheckConstraint("CK_commodity_price_source_settings_source_kind", SourceKindCheckSql);
        });

        builder.HasKey(s => s.Source);

        builder.Property(s => s.Source)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(s => s.IsEnabled)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(s => s.Config)
            .HasColumnType("jsonb");

        builder.Property(s => s.PairAllowList)
            .HasColumnType("jsonb");

        builder.Property(s => s.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(s => s.UpdatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasOne(s => s.CreatedByUser)
            .WithMany()
            .HasForeignKey(s => s.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(s => s.UpdatedByUser)
            .WithMany()
            .HasForeignKey(s => s.UpdatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
