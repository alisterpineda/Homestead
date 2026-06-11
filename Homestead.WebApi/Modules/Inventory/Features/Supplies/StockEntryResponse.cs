using Homestead.WebApi.Modules.Inventory.Features.Items;

namespace Homestead.WebApi.Modules.Inventory.Features.Supplies;

/// <summary>
/// One line of a supply's per-location breakdown: how much sits at one place. The holding location
/// is exactly one of an area or a container; <see cref="Path"/> is the resolved breadcrumb
/// root→holding-location and <see cref="LocationLabel"/> its joined display string.
/// </summary>
public record StockEntryResponse
{
    public required Guid Id { get; init; }
    public required decimal Quantity { get; init; }

    public Guid? AreaId { get; init; }
    public Guid? ContainerItemId { get; init; }

    /// <summary>Resolved path root→holding-location; the last segment is the holding area/container.</summary>
    public required IReadOnlyList<LocationSegmentResponse> Path { get; init; }

    /// <summary>Joined breadcrumb of <see cref="Path"/>, e.g. <c>Garage › Bin</c>.</summary>
    public string? LocationLabel { get; init; }
}
