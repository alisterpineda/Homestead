namespace Homestead.WebApi.Features.Admin.v1.BulkDeleteUsers;

public record BulkDeleteUsersRequest(List<Guid> Ids);
