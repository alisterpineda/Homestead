using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Admin.Features.Users.BulkDeleteUsers;

public record BulkDeleteUsersCommand(List<Guid> Ids) : ICommand<BulkDeleteUsersResponse>;
