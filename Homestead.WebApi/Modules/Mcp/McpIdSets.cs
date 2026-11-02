namespace Homestead.WebApi.Modules.Mcp;

// Id lists on the wire: the per-tool caps, and not_found_ids for the bulk tools. The bulk handlers silently skip ids
// they cannot see (nonexistent, trashed), so the tools report requested − accessible themselves.
public static class McpIdSets
{
    public const int BulkUpdateMaxIds = 100;
    public const int TrashMaxIds = 20;

    // 1..max distinct ids, in request order; otherwise an ArgumentException naming the parameter and the cap (the
    // invoker reports it as "Invalid input: …").
    public static IReadOnlyList<Guid> RequireIds(IReadOnlyList<Guid>? ids, int max, string parameterName)
    {
        var distinct = (ids ?? []).Distinct().ToList();
        if (distinct.Count == 0)
            throw new ArgumentException($"{parameterName} must list at least one document id.");
        if (distinct.Count > max)
            throw new ArgumentException(
                $"{parameterName} takes at most {max} document ids per call, got {distinct.Count}; split the request.");
        return distinct;
    }

    // requested − found, in request order, each id once.
    public static IReadOnlyList<Guid> Missing(IEnumerable<Guid> requested, IEnumerable<Guid> found)
    {
        var foundSet = found.ToHashSet();
        return requested.Distinct().Where(id => !foundSet.Contains(id)).ToList();
    }
}
