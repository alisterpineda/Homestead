using Homestead.WebApi.Common;

namespace Homestead.WebApi.Features.Admin.v1.GetUsers;

public record GetUsersRequest : PaginatedRequest
{
    public string? Search { get; init; }
}
