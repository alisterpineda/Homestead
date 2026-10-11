namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.AdjustStock;

/// <summary>Body of an adjustment (recount): sets the on-hand at one location to an absolute value.</summary>
public record AdjustStockRequest
{
    public Guid? AreaId { get; init; }
    public Guid? ContainerItemId { get; init; }

    /// <summary>The corrected absolute quantity at this location (zero removes the entry).</summary>
    public required decimal NewQuantity { get; init; }
    public DateTimeOffset? OccurredAt { get; init; }
    public string? Note { get; init; }
}
