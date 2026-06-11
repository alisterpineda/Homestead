using Homestead.WebApi.Domain.Inventory;

namespace Homestead.WebApi.Modules.Inventory.Features.Supplies;

/// <summary>
/// One row of the append-only movement ledger. Direction (inflow / outflow / transfer) is read by
/// the client from which of the From*/To* ids are set; a fully-null end is a since-deleted location,
/// rendered with a null label. <see cref="Kind"/> separates real flow from bookkeeping.
/// </summary>
public record StockMovementResponse
{
    public required Guid Id { get; init; }
    public required MovementKind Kind { get; init; }
    public required decimal Quantity { get; init; }

    public Guid? FromAreaId { get; init; }
    public Guid? FromContainerItemId { get; init; }
    public string? FromLabel { get; init; }

    public Guid? ToAreaId { get; init; }
    public Guid? ToContainerItemId { get; init; }
    public string? ToLabel { get; init; }

    public decimal? UnitCost { get; init; }
    public Guid? UnitCostCurrencyId { get; init; }
    public string? UnitCostCurrencySymbol { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    public Guid? RelatedItemId { get; init; }
    public string? RelatedItemName { get; init; }

    public Guid? CreatedByUserId { get; init; }
    public string? CreatedByDisplayName { get; init; }

    public string? Note { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
