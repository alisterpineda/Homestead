using Homestead.WebApi.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Homestead.WebApi.Persistence.Configurations;

public class ItemNoteConfiguration : IEntityTypeConfiguration<ItemNote>
{
    public void Configure(EntityTypeBuilder<ItemNote> builder)
    {
        builder.ToTable("item_notes", "inventory");

        builder.HasKey(n => n.Id);

        // Never empty: the validators reject a blank body (activity.md §The note), as DocumentComment's do.
        builder.Property(n => n.Body)
            .IsRequired()
            .HasMaxLength(4000);

        // The handlers stamp all three instants from the server clock; no DB default needed.
        builder.Property(n => n.OccurredAt).IsRequired();
        builder.Property(n => n.CreatedAt).IsRequired();
        builder.Property(n => n.UpdatedAt).IsRequired();

        // Cascade matches DocumentComment. The delete *gate* — "an item with notes can't be hard-deleted,
        // archive it instead" — lives in DeleteDurable/DeleteConsumable (activity.md §Delete gate), so the
        // FK never fires on its own; it only keeps a bypassed gate from leaving orphans.
        builder.HasOne(n => n.Item)
            .WithMany()
            .HasForeignKey(n => n.ItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(n => n.CreatedByUser)
            .WithMany()
            .HasForeignKey(n => n.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(n => n.UpdatedByUser)
            .WithMany()
            .HasForeignKey(n => n.UpdatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        // Feed reads are per-item, newest-first by the effective date.
        builder.HasIndex(n => new { n.ItemId, n.OccurredAt });
        builder.HasIndex(n => n.CreatedByUserId);
    }
}
