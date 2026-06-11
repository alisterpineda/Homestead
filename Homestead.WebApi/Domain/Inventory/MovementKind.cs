namespace Homestead.WebApi.Domain.Inventory;

/// <summary>
/// Separates a movement's <i>cause</i> from its structural direction (which is read from the
/// location fields, see <see cref="StockMovement"/>). Every value earns its place by changing what
/// consumption analytics count.
/// </summary>
public enum MovementKind
{
    /// <summary>A genuine real-world stock event: an acquisition (inflow), a use (outflow), or a
    /// transfer (both ends). The only kind counted as consumption.</summary>
    Flow,

    /// <summary>A recount/correction reconciling the recorded number to physical reality. Excluded
    /// from consumption analytics so a stocktake never distorts burn rate.</summary>
    Adjustment,

    /// <summary>Quantity moved because of <i>track-this-one-individually</i> or its reverse, not
    /// because anything was bought or used. Carries <see cref="StockMovement.RelatedItemId"/>.</summary>
    Conversion,
}
