using Homestead.WebApi.Domains.Groups;

namespace Homestead.WebApi.Features.Documents.v1.GetMyGroups;

public record GetMyGroupsResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required GroupAccessLevel AccessLevel { get; init; }
}
