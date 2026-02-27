namespace Homestead.WebApi.Features.Tags.v1.GetTags;

public record GetTagsResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Path { get; init; }
    public Guid? ParentId { get; init; }
}
