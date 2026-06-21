using Homestead.WebApi.Common.Search;

namespace Homestead.WebApi.Modules.Search.Features.UniversalSearch;

/// <summary>
/// Universal-search result: the echoed query plus one group per source that returned matches.
/// Groups are per-source — there is no global cross-source ranking.
/// </summary>
public record SearchResponse(string Query, IReadOnlyList<SearchGroup> Groups);

/// <summary>
/// One source's slice of the results.
/// </summary>
/// <param name="Source">Source key (e.g. "inventory") — drives the frontend presenter lookup.</param>
/// <param name="Label">Human-facing group heading (e.g. "Inventory").</param>
/// <param name="Total">Full match count (uncapped) — drives "View all (N)".</param>
/// <param name="Results">The capped, source-ranked hits.</param>
public record SearchGroup(string Source, string Label, int Total, IReadOnlyList<SearchHit> Results);
