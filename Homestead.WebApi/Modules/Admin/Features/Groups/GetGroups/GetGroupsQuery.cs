using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Admin.Features.Groups.GetGroups;

public record GetGroupsQuery : PaginatedRequest
{
    public string? Search { get; init; }
}
