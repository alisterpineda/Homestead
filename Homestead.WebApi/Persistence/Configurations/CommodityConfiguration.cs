using Homestead.WebApi.Domain.Finances;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homestead.WebApi.Persistence.Configurations;

public class CommodityConfiguration : IEntityTypeConfiguration<Commodity>
{
    private const string ExchangeMatchesKindSql =
        "(\"Kind\" = 'Currency' AND \"Exchange\" IS NULL) OR "
        + "(\"Kind\" = 'Security' AND \"Exchange\" IS NOT NULL)";

    private const string DisplayPrecisionRangeSql =
        "\"DisplayPrecision\" >= 0 AND \"DisplayPrecision\" <= 10";

    public void Configure(EntityTypeBuilder<Commodity> builder)
    {
        builder.ToTable("commodities", "finances", table =>
        {
            table.HasCheckConstraint("CK_commodities_exchange_matches_kind", ExchangeMatchesKindSql);
            table.HasCheckConstraint("CK_commodities_display_precision_range", DisplayPrecisionRangeSql);
        });

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Symbol)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(c => c.Exchange)
            .HasMaxLength(16);

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(c => c.Kind)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(c => c.DisplayPrecision)
            .IsRequired();

        builder.Property(c => c.IsSystemManaged)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(c => c.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(c => c.UpdatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasOne(c => c.CreatedByUser)
            .WithMany()
            .HasForeignKey(c => c.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(c => c.UpdatedByUser)
            .WithMany()
            .HasForeignKey(c => c.UpdatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        // Currencies: unique on Symbol alone (Exchange is always null).
        builder.HasIndex(c => c.Symbol)
            .IsUnique()
            .HasFilter("\"Exchange\" IS NULL")
            .HasDatabaseName("IX_commodities_Symbol_currency");

        // Securities: unique on (Symbol, Exchange).
        builder.HasIndex(c => new { c.Symbol, c.Exchange })
            .IsUnique()
            .HasFilter("\"Exchange\" IS NOT NULL")
            .HasDatabaseName("IX_commodities_Symbol_Exchange_security");
    }
}
