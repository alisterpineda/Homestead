namespace Homestead.WebApi.Features.Documents.v1.GetTags;

public record GetTagsResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Path { get; init; }
    public Guid? ParentId { get; init; }
}
