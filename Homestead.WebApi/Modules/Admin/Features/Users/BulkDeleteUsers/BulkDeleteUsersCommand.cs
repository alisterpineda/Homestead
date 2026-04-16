using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.Users.BulkDeleteUsers;

public record BulkDeleteUsersCommand(List<Guid> Ids) : IRequest<BulkDeleteUsersResponse>;
