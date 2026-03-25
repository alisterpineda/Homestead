using MediatR;

namespace Homestead.WebApi.Features.Admin.Users.BulkDeleteUsers;

public record BulkDeleteUsersCommand(List<Guid> Ids) : IRequest<BulkDeleteUsersResponse>;
