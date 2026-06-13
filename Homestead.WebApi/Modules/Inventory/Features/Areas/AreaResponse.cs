namespace Homestead.WebApi.Modules.Inventory.Features.Areas;

public record AreaResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }

    /// <summary>Parent area, or null for a top-level (root) area.</summary>
    public Guid? ParentAreaId { get; init; }

    /// <summary>Parent area's name, resolved for display in the management table; null at root.</summary>
    public string? ParentAreaName { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// Resolved count of live rows the area-filtered items list shows: items resolving here
    /// (directly placed + nested in containers) plus distinct supplies with stock here.
    /// </summary>
    public required int LiveItemCount { get; init; }
}
