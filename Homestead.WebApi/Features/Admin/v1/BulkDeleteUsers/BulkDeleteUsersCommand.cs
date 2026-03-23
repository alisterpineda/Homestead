using MediatR;

namespace Homestead.WebApi.Features.Admin.v1.BulkDeleteUsers;

public record BulkDeleteUsersCommand(List<Guid> Ids) : IRequest<BulkDeleteUsersResponse>;
