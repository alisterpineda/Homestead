using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Features.Admin.Users.GetUsers;

public record GetUsersQuery : PaginatedRequest, IRequest<PaginatedResponse<UserResponse>>
{
    public string? Search { get; init; }
}
