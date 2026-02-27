namespace Homestead.WebApi.Features.Documents.v1.CreateTag;

public record CreateTagResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Path { get; init; }
    public Guid? ParentId { get; init; }
}
