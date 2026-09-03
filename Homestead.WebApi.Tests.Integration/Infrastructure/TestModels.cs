namespace Homestead.WebApi.Tests.Integration.Infrastructure;

internal record JobResponse(Guid JobId);

internal record PaginatedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

internal record PaginatedResultWithSuggestions<T>(
    IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, IReadOnlyList<string> Suggestions);

internal record BulkDeleteResult(int DeletedCount);

internal record BulkReprocessResult(IReadOnlyList<Guid> JobIds);

internal record BulkUpdateResult(int UpdatedCount);

internal record UserItem(Guid Id, string DisplayName, string? Email, string Role, bool IsActive, DateTimeOffset CreatedAt);

internal record UserInfoResult(Guid Id, string Email, string DisplayName, string Role, bool IsActive);

internal record GroupItem(Guid Id, string Name, string? Description, int MemberCount,
    DateTimeOffset UpdatedAt = default, Guid? UpdatedByUserId = null);

internal record GroupDetail(Guid Id, string Name, string? Description, IReadOnlyList<GroupMemberItem> Members);

internal record GroupMemberItem(Guid UserId, string DisplayName, string? Email, string AccessLevel);

internal record DocumentListItem(Guid Id, string Name, Guid? GroupId, string? GroupName,
    DateTimeOffset? DeletedAt = null, Guid? DeletedByUserId = null, string? DeletedByUserName = null,
    DateTimeOffset UpdatedAt = default, Guid? UpdatedByUserId = null);

internal record VersionItem(Guid Id, int VersionNumber, string? Label, string ContentType, long FileSize, string FileExtension);

internal record ProblemDetailsResponse(string? Type, string? Title, int? Status, string? Detail, string? ErrorCode);

internal record DocumentSettingsItem(bool? AutoClassifyOnUpload = null);

internal record BulkClassifyResult(IReadOnlyList<Guid> JobIds);

internal record ClassifyResult(Guid JobId);

internal record SummarizeResult(string? Summary);

internal record DocumentSuggestionItem(Guid Id, string Field, string Status, string SuggestedValue, string? OriginalValue);

internal record ClassifyPreviewResult(
    string? SuggestedTitle, string? Summary, IReadOnlyList<ClassifyPreviewTagItem> Tags);

internal record ClassifyPreviewTagItem(Guid Id, string Name, string Path, string? Color, string? EffectiveColor);

internal record DocumentDetailItem(
    Guid Id, string Name, string? Summary,
    IReadOnlyList<DocumentSuggestionItem>? Suggestions = null);

internal record SystemSettingsItem();

// Finances — enum fields are serialized as strings server-side, so use string here.
internal record FinanceCommodityItem(
    Guid Id, string Symbol, string? Exchange, string Name, string Kind,
    short DisplayPrecision, bool IsSystemManaged,
    int PriceCount = 0, DateTimeOffset? LatestPriceAt = null, bool CanDelete = false);

internal record FinanceAccountItem(
    Guid Id, string Name, string AccountType, string? Kind,
    Guid ReportingCurrencyId, string? Institution,
    bool IsSystem, bool IsArchived,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    decimal? DisplayValue = null, decimal? NativeBalance = null,
    int UnpricedHoldingCount = 0, bool HasTransactions = false,
    DateTimeOffset? LatestTransactionDate = null);

internal record FinancePositionItem(
    Guid CommodityId, decimal Quantity,
    decimal? DisplayValue, decimal? ShareOfAccount);

internal record FinancePriceItem(
    Guid Id, Guid CommodityId, Guid QuoteCurrencyId, DateTimeOffset PriceAt,
    decimal Rate, string Source, Guid? CreatedByTransactionId);

internal record FinanceSettingsItem(Guid? DisplayCurrencyId = null);
