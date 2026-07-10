namespace Homestead.WebApi.Modules.Inventory.Services;

/// <summary>
/// A stock location: exactly one of an area or a container item. A stock entry and both ends of a
/// movement are addressed by this.
/// </summary>
public readonly record struct StockLocation(Guid? AreaId, Guid? ContainerItemId)
{
    public static StockLocation InArea(Guid areaId) => new(areaId, null);
    public static StockLocation InContainer(Guid containerItemId) => new(null, containerItemId);

    /// <summary>True when exactly one of the two location kinds is set.</summary>
    public bool IsValid => (AreaId is null) ^ (ContainerItemId is null);
}

/// <summary>
/// The single funnel for every fungible quantity change. Each operation applies the delta to the
/// relevant stock entry (creating, merging, or deleting it) <b>and</b> appends the matching movement
/// row — both, or neither. Callers (command handlers) own the transaction: the service never opens
/// one and never calls <c>SaveChangesAsync</c>; the per-command pipeline commits exactly once.
/// </summary>
public interface IStockService
{
    /// <summary>Receive stock into a location (inflow). Optionally captures a per-unit cost.</summary>
    Task ReceiveAsync(Guid itemId, StockReceiveRequest request, CancellationToken ct);

    /// <summary>Use stock from a location (outflow). Rejected if it would exceed on-hand there.</summary>
    Task ConsumeAsync(Guid itemId, StockConsumeRequest request, CancellationToken ct);

    /// <summary>Move stock between two locations (one movement, both ends set; nets to zero on-hand).</summary>
    Task TransferAsync(Guid itemId, StockTransferRequest request, CancellationToken ct);

    /// <summary>Correct the recorded count at a location to an absolute value (a recount/Adjustment).</summary>
    Task AdjustAsync(Guid itemId, StockAdjustRequest request, CancellationToken ct);

    /// <summary>
    /// Carve one unit out of fungible stock to be tracked individually: decrement by 1 at a location
    /// and append a <see cref="Domain.Inventory.MovementKind.Conversion"/> outflow carrying the new
    /// non-fungible item's id. Lossless and atomic; requires a whole-number quantity ≥ 1 at the source.
    /// </summary>
    Task ConvertOutAsync(Guid itemId, StockConvertOutRequest request, CancellationToken ct);

    /// <summary>
    /// Collapse individually-tracked instances back into fungible stock: add their count at a location
    /// and append one quantity-1 <see cref="Domain.Inventory.MovementKind.Conversion"/> inflow per
    /// instance, each carrying its item id. The caller archives the instances in the same transaction.
    /// </summary>
    Task ConvertInAsync(Guid itemId, StockConvertInRequest request, CancellationToken ct);
}

public record StockReceiveRequest(
    StockLocation To,
    decimal Quantity,
    decimal? UnitCost,
    Guid? UnitCostCurrencyId,
    DateTimeOffset? OccurredAt,
    string? Note,
    string? Source);

public record StockConsumeRequest(
    StockLocation From,
    decimal Quantity,
    DateTimeOffset? OccurredAt,
    string? Note);

public record StockTransferRequest(
    StockLocation From,
    StockLocation To,
    decimal Quantity,
    DateTimeOffset? OccurredAt,
    string? Note);

public record StockAdjustRequest(
    StockLocation Location,
    decimal NewQuantity,
    DateTimeOffset? OccurredAt,
    string? Note);

public record StockConvertOutRequest(
    StockLocation From,
    Guid RelatedItemId,
    DateTimeOffset? OccurredAt,
    string? Note);

public record StockConvertInRequest(
    StockLocation To,
    IReadOnlyList<Guid> RelatedItemIds,
    DateTimeOffset? OccurredAt,
    string? Note);
