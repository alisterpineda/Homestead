using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.CreateItem;

/// <summary>
/// Creates a non-fungible item. The server hard-codes <c>Fungibility = NonFungible</c>,
/// <c>Status = Active</c>, and <c>IsContainer = false</c>; those are not client-settable in v1.
/// </summary>
public record CreateItemCommand : ICommand<ItemResponse>
{
    public required string Name { get; init; }
    public Guid? AreaId { get; init; }
    public string? Notes { get; init; }
    public string? Serial { get; init; }
    public DateOnly? AcquiredOn { get; init; }
    public decimal? Value { get; init; }
    public Guid? ValueCurrencyId { get; init; }
}
