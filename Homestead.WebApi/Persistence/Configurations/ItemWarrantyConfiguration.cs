using Homestead.WebApi.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homestead.WebApi.Persistence.Configurations;

public class ItemWarrantyConfiguration : IEntityTypeConfiguration<ItemWarranty>
{
    public void Configure(EntityTypeBuilder<ItemWarranty> builder)
    {
        builder.ToTable("item_warranties", "inventory");

        builder.HasKey(w => w.Id);

        // All three user fields are optional: null Provider means "manufacturer", null ExpiresOn means
        // "no expiry", and an all-null row is a valid lifetime manufacturer warranty (warranties.md
        // §The warranty). ExpiresOn is left bare — Npgsql maps DateOnly to `date`, as Item.AcquiredOn.
        builder.Property(w => w.Provider).HasMaxLength(200);
        builder.Property(w => w.Notes).HasMaxLength(500);

        // The handlers stamp both instants from the server clock; no DB default needed.
        builder.Property(w => w.CreatedAt).IsRequired();
        builder.Property(w => w.UpdatedAt).IsRequired();

        // Cascade with no delete gate — deleting a durable takes its warranties with it (unlike notes,
        // which block the delete). Archiving keeps the rows (warranties.md §Delete semantics).
        builder.HasOne(w => w.Item)
            .WithMany(i => i.Warranties)
            .HasForeignKey(w => w.ItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(w => w.CreatedByUser)
            .WithMany()
            .HasForeignKey(w => w.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(w => w.UpdatedByUser)
            .WithMany()
            .HasForeignKey(w => w.UpdatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        // Reads are per-item ordered by expiry (the section list, the derived status, the overview tile).
        builder.HasIndex(w => new { w.ItemId, w.ExpiresOn });
        builder.HasIndex(w => w.CreatedByUserId);
    }
}
