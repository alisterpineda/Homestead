using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Domain.Identity;

namespace Homestead.WebApi.Domain.Inventory;

/// <summary>
/// One append-only row in the movement ledger — every change to a fungible item's quantity writes
/// exactly one, in the same transaction as the stock-entry change. Direction (inflow / outflow /
/// internal transfer) is <i>read from</i> the From*/To* location fields, never stored; <see cref="Kind"/>
/// only separates real flow from bookkeeping. <see cref="Quantity"/> is a positive magnitude.
/// Location and user references are <c>SET NULL</c> on delete: a deleted area/container/user clears
/// the now-meaningless label while the recorded quantity and what happened to it survive.
/// </summary>
public class StockMovement
{
    public Guid Id { get; set; }

    /// <summary>The fungible item this movement is of.</summary>
    public Guid ItemId { get; set; }
    public Item Item { get; set; } = null!;

    public MovementKind Kind { get; set; }

    // Source location (nullable). Set + null destination = outflow.
    public Guid? FromAreaId { get; set; }
    public Area? FromArea { get; set; }
    public Guid? FromContainerItemId { get; set; }
    public Item? FromContainerItem { get; set; }

    // Destination location (nullable). Null source + set = inflow. Both set = internal transfer.
    public Guid? ToAreaId { get; set; }
    public Area? ToArea { get; set; }
    public Guid? ToContainerItemId { get; set; }
    public Item? ToContainerItem { get; set; }

    /// <summary>Positive magnitude of the change, in the item's unit.</summary>
    public decimal Quantity { get; set; }

    /// <summary>Optional per-unit cost, paired both-or-neither with <see cref="UnitCostCurrencyId"/>.</summary>
    public decimal? UnitCost { get; set; }
    public Guid? UnitCostCurrencyId { get; set; }
    public Commodity? UnitCostCurrency { get; set; }

    /// <summary>When it happened (UTC; back-dating allowed).</summary>
    public DateTimeOffset OccurredAt { get; set; }

    public Guid? CreatedByUserId { get; set; }
    public AppUser? CreatedByUser { get; set; }

    /// <summary>For a <see cref="MovementKind.Conversion"/>: the non-fungible item carved out / collapsed.</summary>
    public Guid? RelatedItemId { get; set; }
    public Item? RelatedItem { get; set; }

    public string? Note { get; set; }

    /// <summary>When the row was recorded (UTC); distinct from <see cref="OccurredAt"/>.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
