using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.Groups.GetGroups;

public record GetGroupsQuery : PaginatedRequest, IRequest<PaginatedResponse<GroupResponse>>
{
    public string? Search { get; init; }
}
