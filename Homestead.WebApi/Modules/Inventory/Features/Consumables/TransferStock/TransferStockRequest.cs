namespace Homestead.WebApi.Modules.Inventory.Features.Consumables.TransferStock;

/// <summary>Body of a transfer: moves stock between two locations as one movement (nets to zero).</summary>
public record TransferStockRequest
{
    public Guid? FromAreaId { get; init; }
    public Guid? FromContainerItemId { get; init; }
    public Guid? ToAreaId { get; init; }
    public Guid? ToContainerItemId { get; init; }
    public required decimal Quantity { get; init; }
    public DateTimeOffset? OccurredAt { get; init; }
    public string? Note { get; init; }
}
