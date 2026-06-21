namespace Homestead.WebApi.Common.Search;

/// <summary>
/// A module's contribution to universal search. One implementation per searchable module
/// (Inventory, later Documents/Finances/Admin). The universal-search handler fans out to every
/// registered source and returns one result group per source — no cross-source ranking.
/// Implementations depend only on this Common contract, so adding a module is additive: implement
/// one source and register it; the endpoint and handler never change.
/// </summary>
public interface ISearchSource
{
    /// <summary>Stable machine key for the source (e.g. "inventory"). Drives the frontend presenter lookup.</summary>
    string Source { get; }

    /// <summary>Human-facing group label (e.g. "Inventory").</summary>
    string Label { get; }

    /// <summary>
    /// Run the source's own search for <paramref name="query"/>, returning the full match count and
    /// up to <paramref name="limit"/> hits. The handler guarantees a trimmed query of length &gt;= 2.
    /// </summary>
    Task<SearchSourceResult> SearchAsync(string query, int limit, CancellationToken ct);
}
