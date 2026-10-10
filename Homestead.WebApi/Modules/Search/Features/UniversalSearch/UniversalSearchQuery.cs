using Homestead.WebApi.Common.Search;

namespace Homestead.WebApi.Modules.Search.Features.UniversalSearch;

/// <summary>
/// Cross-module universal-search request. Bound from the query string via <c>[AsParameters]</c>.
/// </summary>
public record UniversalSearchQuery
{
    /// <summary>The search text (bound from <c>?Q=</c>). Trimmed; shorter than 2 chars returns no groups.</summary>
    public string? Q { get; init; }

    /// <summary>Per-source result cap. Default 6, clamped to 1..20.</summary>
    public int? Limit { get; init; }
}
