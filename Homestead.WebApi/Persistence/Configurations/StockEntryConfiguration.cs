using Homestead.WebApi.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homestead.WebApi.Persistence.Configurations;

public class StockEntryConfiguration : IEntityTypeConfiguration<StockEntry>
{
    // Exactly one location is set: a stock entry exists because there is quantity at a place, so —
    // unlike a non-fungible item — it can be neither placeless nor in two places.
    private const string LocationExactlyOneSql =
        "(\"AreaId\" IS NOT NULL AND \"ContainerItemId\" IS NULL) OR "
        + "(\"AreaId\" IS NULL AND \"ContainerItemId\" IS NOT NULL)";

    // Quantity never goes negative — the last-resort backstop for an over-draw that races the
    // handler's on-hand check.
    private const string QuantityNonNegativeSql = "\"Quantity\" >= 0";

    public void Configure(EntityTypeBuilder<StockEntry> builder)
    {
        builder.ToTable("stock_entries", "inventory", table =>
        {
            table.HasCheckConstraint("CK_stock_entries_location_exactly_one", LocationExactlyOneSql);
            table.HasCheckConstraint("CK_stock_entries_quantity_non_negative", QuantityNonNegativeSql);
        });

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Quantity)
            .IsRequired()
            .HasColumnType("numeric");

        builder.Property(e => e.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(e => e.UpdatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        // At most one entry per (item, location). Two of the three columns are nullable and Postgres
        // treats nulls as distinct by default, so the merge rule only holds with NULLS NOT DISTINCT.
        builder.HasIndex(e => new { e.ItemId, e.AreaId, e.ContainerItemId })
            .IsUnique()
            .AreNullsDistinct(false);

        builder.HasOne(e => e.Item)
            .WithMany(i => i.StockEntries)
            .HasForeignKey(e => e.ItemId)
            .OnDelete(DeleteBehavior.Cascade);

        // A live stock entry pins its location: an area / container can only be deleted once no stock
        // sits in it (handler-enforced; the Restrict FK is the backstop).
        builder.HasOne(e => e.Area)
            .WithMany()
            .HasForeignKey(e => e.AreaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ContainerItem)
            .WithMany()
            .HasForeignKey(e => e.ContainerItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.ItemId);
        builder.HasIndex(e => e.AreaId);
        builder.HasIndex(e => e.ContainerItemId);
    }
}
