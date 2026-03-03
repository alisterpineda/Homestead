namespace Homestead.WebApi.Tests.Integration.Infrastructure;

internal record JobResponse(Guid JobId);

internal record PaginatedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

internal record BulkDeleteResult(int DeletedCount);

internal record UserItem(Guid Id, string DisplayName, string? Email, string Role, bool IsActive, DateTimeOffset CreatedAt);

internal record UserInfoResult(Guid Id, string Email, string DisplayName, string Role, bool IsActive);

internal record GroupItem(Guid Id, string Name, string? Description, int MemberCount);

internal record GroupDetail(Guid Id, string Name, string? Description, IReadOnlyList<GroupMemberItem> Members);

internal record GroupMemberItem(Guid UserId, string DisplayName, string? Email, string AccessLevel);

internal record DocumentListItem(Guid Id, string Name, Guid? GroupId, string? GroupName);
