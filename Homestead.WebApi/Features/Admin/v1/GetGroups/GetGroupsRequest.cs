using Homestead.WebApi.Common;

namespace Homestead.WebApi.Features.Admin.v1.GetGroups;

public record GetGroupsRequest : PaginatedRequest
{
    public string? Search { get; init; }
}
