namespace Homestead.WebApi.Modules.Inventory.Features.Areas;

public record AreaResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Count of live (directly-placed, non-terminal) items currently in this area.</summary>
    public required int LiveItemCount { get; init; }
}
