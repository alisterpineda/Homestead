namespace Homestead.WebApi.Tests.Integration.Infrastructure;

internal record JobResponse(Guid JobId);

internal record PaginatedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

internal record BulkDeleteResult(int DeletedCount);

internal record UserItem(Guid Id, string DisplayName, string? Email, string Role, bool IsActive, DateTimeOffset CreatedAt);

internal record UserInfoResult(Guid Id, string Email, string DisplayName, string Role, bool IsActive);
