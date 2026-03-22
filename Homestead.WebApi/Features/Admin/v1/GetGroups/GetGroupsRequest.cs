using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Features.Admin.v1.GetGroups;

public record GetGroupsRequest : PaginatedRequest, IRequest<PaginatedResponse<GroupResponse>>
{
    public string? Search { get; init; }
}
