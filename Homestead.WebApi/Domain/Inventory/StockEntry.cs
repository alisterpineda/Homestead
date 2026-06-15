using Homestead.WebApi.Domain.Identity;

namespace Homestead.WebApi.Domain.Inventory;

/// <summary>
/// How much of one fungible <see cref="Item"/> sits at one location. A consumable owns no single
/// location or quantity — its presence is the set of its stock entries, and its total on-hand is
/// their sum. Exactly one of <see cref="AreaId"/> / <see cref="ContainerItemId"/> is set; an entry
/// exists <i>because</i> there is quantity there, so a zero-quantity entry is deleted, never kept.
/// There is at most one entry per <c>(ItemId, AreaId, ContainerItemId)</c> — adding stock where the
/// item already sits merges into the existing row.
/// </summary>
public class StockEntry
{
    public Guid Id { get; set; }

    /// <summary>The fungible item this stock is of.</summary>
    public Guid ItemId { get; set; }
    public Item Item { get; set; } = null!;

    /// <summary>Location: an area … (exactly one of this / <see cref="ContainerItemId"/>).</summary>
    public Guid? AreaId { get; set; }
    public Area? Area { get; set; }

    /// <summary>… or a non-fungible container item (exactly one of this / <see cref="AreaId"/>).</summary>
    public Guid? ContainerItemId { get; set; }
    public Item? ContainerItem { get; set; }

    /// <summary>How much, in the item's unit; constrained <c>&gt;= 0</c>.</summary>
    public decimal Quantity { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
