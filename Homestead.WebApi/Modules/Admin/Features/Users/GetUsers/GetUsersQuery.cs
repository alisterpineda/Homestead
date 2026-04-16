using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.Users.GetUsers;

public record GetUsersQuery : PaginatedRequest, IRequest<PaginatedResponse<UserResponse>>
{
    public string? Search { get; init; }
}
