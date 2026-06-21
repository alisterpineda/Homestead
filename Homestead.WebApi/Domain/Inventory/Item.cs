using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Domain.Identity;

namespace Homestead.WebApi.Domain.Inventory;

/// <summary>
/// Anything the household owns. A single table holds both kinds, discriminated by
/// <see cref="Fungibility"/>; the kind-specific columns are null on the other kind, enforced by
/// check constraints. v1 surfaces non-fungible durables only; the fungible columns exist so the
/// stock model can land later without re-migrating this table.
/// </summary>
public class Item
{
    // Shared (all items)
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public Fungibility Fungibility { get; set; }
    public ItemStatus Status { get; set; }
    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? CreatedByUserId { get; set; }
    public AppUser? CreatedByUser { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public AppUser? UpdatedByUser { get; set; }

    // Non-fungible
    /// <summary>Direct area placement. At most one of <see cref="AreaId"/> / <see cref="ParentItemId"/> is set.</summary>
    public Guid? AreaId { get; set; }
    public Area? Area { get; set; }

    /// <summary>Container placement (self-reference). At most one of this / <see cref="AreaId"/> is set.</summary>
    public Guid? ParentItemId { get; set; }
    public Item? ParentItem { get; set; }

    /// <summary>
    /// Holder-role; set on every non-fungible item, null on fungible items. A <see cref="DurableKind.Container"/>
    /// stores children and stock; an <see cref="DurableKind.Assembly"/> holds components; a
    /// <see cref="DurableKind.Plain"/> item holds nothing and cannot be a parent.
    /// </summary>
    public DurableKind? DurableKind { get; set; }

    /// <summary>
    /// Free-text sub-position within a parent ("top drawer"); doubles as the component role/slot
    /// label ("CPU") for a child of an <see cref="DurableKind.Assembly"/>.
    /// </summary>
    public string? Compartment { get; set; }

    /// <summary>Estimated value; paired both-or-neither with <see cref="ValueCurrencyId"/>.</summary>
    public decimal? Value { get; set; }
    public Guid? ValueCurrencyId { get; set; }
    public Commodity? ValueCurrency { get; set; }

    public string? Serial { get; set; }
    public DateOnly? AcquiredOn { get; set; }

    /// <summary>Free-text "lent to whom"; set only when <see cref="Status"/> is <see cref="ItemStatus.Lent"/>.</summary>
    public string? LentTo { get; set; }

    // Fungible
    /// <summary>Unit of measure for a consumable ("screws", "rolls", "gal").</summary>
    public string? Unit { get; set; }

    /// <summary>Optional global low-stock trigger compared against total on-hand.</summary>
    public decimal? RestockThreshold { get; set; }

    /// <summary>
    /// Per-location stock of this fungible item. Empty for non-fungible items. The total on-hand is
    /// the sum of these entries' quantities — no denormalised total is stored on the item.
    /// </summary>
    public ICollection<StockEntry> StockEntries { get; set; } = [];
}
