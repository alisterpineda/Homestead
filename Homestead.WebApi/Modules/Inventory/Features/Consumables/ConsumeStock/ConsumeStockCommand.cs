namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.ConsumeStock;

/// <summary>Body of a consume (outflow): uses stock from one location. Rejected if it exceeds on-hand.</summary>
public record ConsumeStockRequest
{
    public Guid? AreaId { get; init; }
    public Guid? ContainerItemId { get; init; }
    public required decimal Quantity { get; init; }
    public DateTimeOffset? OccurredAt { get; init; }
    public string? Note { get; init; }
}

public record ConsumeStockCommand
{
    public Guid ConsumableId { get; init; }
    public required ConsumeStockRequest Body { get; init; }
}
