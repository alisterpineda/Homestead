namespace Homestead.WebApi.Common.Search;

/// <summary>
/// A source's response to a universal-search query.
/// </summary>
/// <param name="Total">Full match count (uncapped) — drives the "View all (N)" deep link.</param>
/// <param name="Hits">The matches, already capped to the requested limit and ranked by the source.</param>
public record SearchSourceResult(int Total, IReadOnlyList<SearchHit> Hits);
