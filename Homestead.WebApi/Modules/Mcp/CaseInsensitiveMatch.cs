using System.Linq.Expressions;

namespace Homestead.WebApi.Modules.Mcp;

// The one way a model-sent name (tag path, party name) finds stored rows. Tag.Path's and Party.Name's unique indexes
// are case-sensitive, so two casings of one name can coexist; matching is case-insensitive and an exact-casing match
// wins. Callers normalise first (TagPathResolver.NormalizePath / PartyNameResolver.NormalizeName).
public static class CaseInsensitiveMatch
{
    // Every row whose name equals one of names in any casing — translated to SQL as lower(name) IN (…).
    public static IQueryable<T> WhereNameIn<T>(
        this IQueryable<T> source, Expression<Func<T, string>> name, IEnumerable<string> names)
    {
        var lowered = names.Select(n => n.ToLowerInvariant()).ToList();
        Expression<Func<string, bool>> matches = n => lowered.Contains(n.ToLower());

        var body = new SubstituteParameter(matches.Parameters[0], name.Body).Visit(matches.Body);
        return source.Where(Expression.Lambda<Func<T, bool>>(body, name.Parameters));
    }

    // Picks one candidate per requested name (distinct, case-insensitively, in request order): the exact casing,
    // else the ordinally first casing. A row chosen by an earlier name is not repeated (one row per key). Names
    // with no candidate are Unknown, in request order.
    public static CaseInsensitiveMatches<T> Choose<T, TKey>(
        IEnumerable<string> requested, IEnumerable<T> candidates, Func<T, TKey> key, Func<T, string> name)
        where T : class
    {
        var rows = candidates.ToList();
        var keyComparer = EqualityComparer<TKey>.Default;
        var known = new List<T>();
        var unknown = new List<string>();

        foreach (var requestedName in requested.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var row = rows.FirstOrDefault(r => string.Equals(name(r), requestedName, StringComparison.Ordinal))
                      ?? rows
                          .Where(r => string.Equals(name(r), requestedName, StringComparison.OrdinalIgnoreCase))
                          .OrderBy(name, StringComparer.Ordinal)
                          .FirstOrDefault();
            if (row is null)
                unknown.Add(requestedName);
            else if (!known.Any(k => keyComparer.Equals(key(k), key(row))))
                known.Add(row);
        }

        return new CaseInsensitiveMatches<T>(known, unknown);
    }

    private sealed class SubstituteParameter(ParameterExpression parameter, Expression replacement) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == parameter ? replacement : base.VisitParameter(node);
    }
}

public sealed record CaseInsensitiveMatches<T>(IReadOnlyList<T> Known, IReadOnlyList<string> Unknown);
