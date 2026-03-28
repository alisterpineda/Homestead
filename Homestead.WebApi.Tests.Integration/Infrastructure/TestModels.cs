namespace Homestead.WebApi.Tests.Integration.Infrastructure;

internal record JobResponse(Guid JobId);

internal record PaginatedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

internal record BulkDeleteResult(int DeletedCount);

internal record BulkReprocessResult(IReadOnlyList<Guid> JobIds);

internal record BulkUpdateResult(int UpdatedCount);

internal record UserItem(Guid Id, string DisplayName, string? Email, string Role, bool IsActive, bool HasPassword, DateTimeOffset CreatedAt);

internal record UserInfoResult(Guid Id, string Email, string DisplayName, string Role, bool IsActive);

internal record GroupItem(Guid Id, string Name, string? Description, int MemberCount,
    DateTimeOffset? UpdatedAt = null, Guid? UpdatedByUserId = null);

internal record GroupDetail(Guid Id, string Name, string? Description, IReadOnlyList<GroupMemberItem> Members);

internal record GroupMemberItem(Guid UserId, string DisplayName, string? Email, string AccessLevel);

internal record DocumentListItem(Guid Id, string Name, Guid? GroupId, string? GroupName,
    DateTimeOffset? DeletedAt = null, Guid? DeletedByUserId = null, string? DeletedByUserName = null,
    DateTimeOffset? UpdatedAt = null, Guid? UpdatedByUserId = null);

internal record VersionItem(Guid Id, int VersionNumber, string? Label, string ContentType, long FileSize, string FileExtension);

internal record ProblemDetailsResponse(string? Type, string? Title, int? Status, string? Detail, string? ErrorCode);

internal record TextExtractionEngineItem(Guid Id, string Name, string EngineType, string? Configuration, bool IsBuiltIn);

internal record DocumentSettingsItem(Guid? DefaultTextExtractionEngineId = null);

internal record SystemSettingsItem();

internal record TextExtractionEngineSummaryItem(Guid Id, string Name);
