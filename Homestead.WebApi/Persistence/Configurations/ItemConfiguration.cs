using Homestead.WebApi.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homestead.WebApi.Persistence.Configurations;

public class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    // At most one location is set: an item sits in an area, in a container, or nowhere (Unplaced).
    private const string LocationNotBothSql =
        "NOT (\"AreaId\" IS NOT NULL AND \"ParentItemId\" IS NOT NULL)";

    // A fungible row carries none of the non-fungible columns and has no durable kind. BrandId is
    // deliberately absent — a brand is legal on both kinds.
    private const string NonFungibleFieldsSql =
        "\"Fungibility\" = 'NonFungible' OR ("
        + "\"AreaId\" IS NULL AND \"ParentItemId\" IS NULL AND \"Compartment\" IS NULL "
        + "AND \"Value\" IS NULL AND \"ValueCurrencyId\" IS NULL AND \"Serial\" IS NULL "
        + "AND \"Model\" IS NULL "
        + "AND \"AcquiredOn\" IS NULL AND \"LentTo\" IS NULL AND \"DurableKind\" IS NULL)";

    // A non-fungible row carries none of the fungible columns and always names a durable kind.
    private const string FungibleFieldsSql =
        "\"Fungibility\" = 'Fungible' OR ("
        + "\"Unit\" IS NULL AND \"RestockThreshold\" IS NULL AND \"DurableKind\" IS NOT NULL)";

    // Estimated value and its currency are set together or not at all.
    private const string ValueCurrencyPairSql =
        "(\"Value\" IS NULL AND \"ValueCurrencyId\" IS NULL) OR "
        + "(\"Value\" IS NOT NULL AND \"ValueCurrencyId\" IS NOT NULL)";

    // LentTo is only meaningful while the item is Lent.
    private const string LentToRequiresLentSql =
        "\"LentTo\" IS NULL OR \"Status\" = 'Lent'";

    // Fungible consumables use the reduced Active/Archived lifecycle only.
    private const string FungibleStatusSql =
        "\"Fungibility\" = 'NonFungible' OR \"Status\" IN ('Active', 'Archived')";

    public void Configure(EntityTypeBuilder<Item> builder)
    {
        builder.ToTable("items", "inventory", table =>
        {
            table.HasCheckConstraint("CK_items_location_not_both", LocationNotBothSql);
            table.HasCheckConstraint("CK_items_nonfungible_fields", NonFungibleFieldsSql);
            table.HasCheckConstraint("CK_items_fungible_fields", FungibleFieldsSql);
            table.HasCheckConstraint("CK_items_value_currency_pair", ValueCurrencyPairSql);
            table.HasCheckConstraint("CK_items_lent_to_requires_lent", LentToRequiresLentSql);
            table.HasCheckConstraint("CK_items_fungible_status", FungibleStatusSql);
        });

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Name)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(i => i.Fungibility)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(i => i.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(i => i.Compartment).HasMaxLength(256);
        builder.Property(i => i.Serial).HasMaxLength(128);
        builder.Property(i => i.Model).HasMaxLength(128);
        builder.Property(i => i.LentTo).HasMaxLength(256);
        builder.Property(i => i.Unit).HasMaxLength(32);

        builder.Property(i => i.Value).HasColumnType("numeric");
        builder.Property(i => i.RestockThreshold).HasColumnType("numeric");

        builder.Property(i => i.DurableKind)
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(i => i.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(i => i.UpdatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        // Terminal items must never block area deletion: their stale label clears (SetNull); the
        // live-item block is handler-enforced.
        builder.HasOne(i => i.Area)
            .WithMany()
            .HasForeignKey(i => i.AreaId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(i => i.ParentItem)
            .WithMany()
            .HasForeignKey(i => i.ParentItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.ValueCurrency)
            .WithMany()
            .HasForeignKey(i => i.ValueCurrencyId)
            .OnDelete(DeleteBehavior.Restrict);

        // A brand in use can't be deleted — mirrors the ValueCurrency FK.
        builder.HasOne(i => i.Brand)
            .WithMany()
            .HasForeignKey(i => i.BrandId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.CreatedByUser)
            .WithMany()
            .HasForeignKey(i => i.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(i => i.UpdatedByUser)
            .WithMany()
            .HasForeignKey(i => i.UpdatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(i => i.AreaId);
        builder.HasIndex(i => i.ParentItemId);
        builder.HasIndex(i => i.Status);
        builder.HasIndex(i => i.BrandId);
    }
}
