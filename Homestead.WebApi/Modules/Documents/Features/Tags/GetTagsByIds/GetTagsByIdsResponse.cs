namespace Homestead.WebApi.Modules.Documents.Features.Tags.GetTagsByIds;

public record GetTagsByIdsResponse
{
    public required Guid Id { get; init; }
    public required string Path { get; init; }
}
