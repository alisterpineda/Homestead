using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.ReceiveStock;

/// <summary>Body of a receive (inflow): adds stock into one location, optionally with a unit cost.</summary>
public record ReceiveStockRequest
{
    public Guid? AreaId { get; init; }
    public Guid? ContainerItemId { get; init; }
    public required decimal Quantity { get; init; }
    public decimal? UnitCost { get; init; }
    public Guid? UnitCostCurrencyId { get; init; }
    public DateTimeOffset? OccurredAt { get; init; }
    public string? Note { get; init; }

    /// <summary>Where the stock came from ("Costco"); free text, receive-only.</summary>
    public string? Source { get; init; }
}

public record ReceiveStockCommand : ICommand<ConsumableDetailResponse>
{
    public Guid ConsumableId { get; init; }
    public required ReceiveStockRequest Body { get; init; }
}
