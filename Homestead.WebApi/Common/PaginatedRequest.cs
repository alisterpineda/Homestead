namespace Homestead.WebApi.Common;

public record PaginatedRequest
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 1000;

    // Nullable so minimal-API [AsParameters] binding treats them as OPTIONAL query params — a
    // non-nullable value type would be bound as REQUIRED and a request omitting page/pageSize
    // would fail binding (BadHttpRequestException → 500) before the handler runs. The default +
    // clamp lives in the Effective* accessors, not a property initializer, because [AsParameters]
    // binds an absent optional property to null (overwriting any initializer) and derived query
    // records bind Page/PageSize as inherited properties regardless of any base ctor default.
    // Read Effective* everywhere a concrete page/size is needed.
    public int? Page { get; init; }
    public int? PageSize { get; init; }

    public int EffectivePage => Page is { } p && p >= 1 ? p : 1;
    public int EffectivePageSize => PageSize is { } s ? Math.Clamp(s, 1, MaxPageSize) : DefaultPageSize;
}
