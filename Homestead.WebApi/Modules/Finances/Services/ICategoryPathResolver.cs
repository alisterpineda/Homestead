namespace Homestead.WebApi.Modules.Finances.Services;

/// <summary>
/// The computed name/path/depth of a user category account, keyed by AccountId. Path is a breadcrumb
/// of ancestor leaf names (root → leaf), e.g. "Food › Groceries". Depth is the ancestor count (root = 0).
/// </summary>
public record CategoryPathInfo(string LeafName, string Path, int Depth);

/// <summary>
/// Resolves the breadcrumb/depth of every <b>user</b> category account (Income/Expense, IsSystem=false)
/// in one in-memory tree walk — mirrors the tag path resolvers. System buckets are intentionally
/// excluded: for transaction mapping, a counterpart leg <i>absent</i> from this map is an uncategorised
/// (system-bucket) flow. Reused by GetCategories, CreateCategory/PatchCategory, and TransactionMapping.
/// </summary>
public interface ICategoryPathResolver
{
    Task<IReadOnlyDictionary<Guid, CategoryPathInfo>> GetPathMapAsync(CancellationToken ct);
}
