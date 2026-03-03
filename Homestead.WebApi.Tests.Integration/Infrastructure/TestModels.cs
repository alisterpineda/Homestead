namespace Homestead.WebApi.Tests.Integration.Infrastructure;

internal record JobResponse(Guid JobId);

internal record PaginatedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

internal record BulkDeleteResult(int DeletedCount);

internal record UserItem(Guid Id, string DisplayName, string? Email, bool IsAdmin, bool IsActive, DateTimeOffset CreatedAt);

internal record UserInfoResult(Guid Id, string Email, string DisplayName, IList<string> Roles, bool IsActive);
