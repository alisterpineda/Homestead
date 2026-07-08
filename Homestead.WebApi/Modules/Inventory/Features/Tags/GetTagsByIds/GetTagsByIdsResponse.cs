namespace Homestead.WebApi.Modules.Inventory.Features.Tags.GetTagsByIds;

public record GetInventoryTagsByIdsResponse
{
    public required Guid Id { get; init; }
    public required string Path { get; init; }
}
