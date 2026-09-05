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

    /// <summary>
    /// Parent placement (self-reference). At most one of this / <see cref="AreaId"/> is set. The
    /// relationship is derived from the parent's <see cref="IsContainer"/>: a child of a container is
    /// stored in it (loose contents); a child of a non-container is a component of it.
    /// </summary>
    public Guid? ParentItemId { get; set; }
    public Item? ParentItem { get; set; }

    /// <summary>Inverse of <see cref="ParentItem"/> — direct children (stored-in contents or components).</summary>
    public ICollection<Item> Children { get; set; } = [];

    /// <summary>
    /// Whether this item is a storage container; set on every non-fungible item, null on fungible
    /// items. <c>true</c> = a browsable place holding loose stock and stored-in children; <c>false</c>
    /// = a regular item whose children (if any) are its components. Assembly-ness (having components)
    /// is not stored — it is derived from having component children of a non-container.
    /// </summary>
    public bool? IsContainer { get; set; }

    /// <summary>
    /// Free-text sub-position within a parent ("top drawer"); doubles as the component role/slot
    /// label ("CPU") for a child of a non-container (<see cref="IsContainer"/> = false).
    /// </summary>
    public string? Compartment { get; set; }

    /// <summary>Estimated value; paired both-or-neither with <see cref="ValueCurrencyId"/>.</summary>
    public decimal? Value { get; set; }
    public Guid? ValueCurrencyId { get; set; }
    public Commodity? ValueCurrency { get; set; }

    /// <summary>
    /// Optional canonical brand this item is attributed to. Applies to both durables and consumables
    /// (the FK lives on the shared row); single-valued and the axis for brand grouping. Restrict on
    /// delete — a brand in use can't be removed.
    /// </summary>
    public Guid? BrandId { get; set; }
    public Brand? Brand { get; set; }

    public string? Serial { get; set; }

    /// <summary>Optional free-text model designation ("MRW23"); durable-only, sits beside <see cref="Serial"/>.</summary>
    public string? Model { get; set; }

    public DateOnly? AcquiredOn { get; set; }

    /// <summary>How the item entered the household; null = not recorded. Durable-only.</summary>
    public AcquisitionKind? AcquisitionKind { get; set; }

    /// <summary>
    /// What was actually paid — a historical fact, distinct from the estimated current
    /// <see cref="Value"/>. Paired both-or-neither with <see cref="AcquisitionCostCurrencyId"/>.
    /// </summary>
    public decimal? AcquisitionCost { get; set; }
    public Guid? AcquisitionCostCurrencyId { get; set; }
    public Commodity? AcquisitionCostCurrency { get; set; }

    /// <summary>Free-text source — a store or a person ("Home Depot", "Mom"); <see cref="LentTo"/> precedent, no vendor entity.</summary>
    public string? AcquiredFrom { get; set; }

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

    /// <summary>
    /// Hierarchical classification tags. Multi-valued across both item kinds; the join carries no
    /// column on the items table (identity lives on <see cref="Tag.Path"/>).
    /// </summary>
    public ICollection<ItemTag> ItemTags { get; set; } = [];

    /// <summary>
    /// Warranty rows on this durable (empty for a consumable). Unlike <see cref="ItemNote"/> — which
    /// deliberately has no inverse — this one exists because <c>DurableResponse.WarrantyStatus</c> is
    /// projected from it in SQL.
    /// </summary>
    public ICollection<ItemWarranty> Warranties { get; set; } = [];
}
