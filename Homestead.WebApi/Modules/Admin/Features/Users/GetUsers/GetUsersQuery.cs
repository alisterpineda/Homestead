using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Admin.Features.Users.GetUsers;

public record GetUsersQuery : PaginatedRequest
{
    public string? Search { get; init; }
}
