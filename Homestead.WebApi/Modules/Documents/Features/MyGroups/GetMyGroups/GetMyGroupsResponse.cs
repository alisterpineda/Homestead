using Homestead.WebApi.Domain.Groups;

namespace Homestead.WebApi.Modules.Documents.Features.MyGroups.GetMyGroups;

public record GetMyGroupsResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required GroupAccessLevel AccessLevel { get; init; }
}
