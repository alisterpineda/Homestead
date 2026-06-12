namespace Homestead.WebApi.Modules.Inventory.Features.Areas;

public record AreaResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// Resolved count of live rows the area-filtered items list shows: items resolving here
    /// (directly placed + nested in containers) plus distinct supplies with stock here.
    /// </summary>
    public required int LiveItemCount { get; init; }
}
