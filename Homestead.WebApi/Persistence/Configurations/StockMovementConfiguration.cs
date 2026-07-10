using Homestead.WebApi.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homestead.WebApi.Persistence.Configurations;

public class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    // Movement quantity is a positive magnitude; direction comes from the location fields.
    private const string QuantityPositiveSql = "\"Quantity\" > 0";

    // Unit cost and its currency are set together or not at all.
    private const string UnitCostCurrencyPairSql =
        "(\"UnitCost\" IS NULL AND \"UnitCostCurrencyId\" IS NULL) OR "
        + "(\"UnitCost\" IS NOT NULL AND \"UnitCostCurrencyId\" IS NOT NULL)";

    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("stock_movements", "inventory", table =>
        {
            table.HasCheckConstraint("CK_stock_movements_quantity_positive", QuantityPositiveSql);
            table.HasCheckConstraint("CK_stock_movements_unit_cost_currency_pair", UnitCostCurrencyPairSql);
        });

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Kind)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(m => m.Quantity)
            .IsRequired()
            .HasColumnType("numeric");

        builder.Property(m => m.UnitCost).HasColumnType("numeric");

        builder.Property(m => m.Note).HasMaxLength(1000);

        builder.Property(m => m.Source).HasMaxLength(256);

        builder.Property(m => m.OccurredAt).IsRequired();

        builder.Property(m => m.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        // The consumable this movement is of: the ledger outlives everything but the item itself, and a
        // fungible item with any movement history can only be archived, never deleted.
        builder.HasOne(m => m.Item)
            .WithMany()
            .HasForeignKey(m => m.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        // Location references clear on delete (SET NULL): a deleted area/container leaves the recorded
        // quantity and what happened to it intact while clearing the now-meaningless label.
        builder.HasOne(m => m.FromArea)
            .WithMany()
            .HasForeignKey(m => m.FromAreaId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(m => m.ToArea)
            .WithMany()
            .HasForeignKey(m => m.ToAreaId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(m => m.FromContainerItem)
            .WithMany()
            .HasForeignKey(m => m.FromContainerItemId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(m => m.ToContainerItem)
            .WithMany()
            .HasForeignKey(m => m.ToContainerItemId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(m => m.UnitCostCurrency)
            .WithMany()
            .HasForeignKey(m => m.UnitCostCurrencyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.CreatedByUser)
            .WithMany()
            .HasForeignKey(m => m.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(m => m.RelatedItem)
            .WithMany()
            .HasForeignKey(m => m.RelatedItemId)
            .OnDelete(DeleteBehavior.SetNull);

        // Ledger reads are per-item, newest-first; usage queries filter by kind.
        builder.HasIndex(m => new { m.ItemId, m.OccurredAt });
        builder.HasIndex(m => m.Kind);
    }
}
