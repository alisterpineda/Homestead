using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;

namespace Homestead.WebApi.Modules.Mcp;

// Maps the tag paths a model sends onto existing tags. Every tag write (TagPathResolver) creates unknown paths, and
// Tag.Path's unique index is case-sensitive, so "home/utilities" would silently become a second taxonomy branch;
// snapping first returns the stored casing and refuses what does not exist. Read-only.
public sealed class TagPathSnapper(AppDbContext db)
{
    private const int MaxNearest = 3;

    // Normalised exactly as a write would (TagPathResolver; an empty path throws ArgumentException, which the
    // invoker reports as invalid input), matched case-insensitively. An exact-casing match wins when two casings of
    // one path exist. Known keeps input order without duplicates; Suggestions lists only unknowns with any nearest.
    public async Task<TagPathSnapResult> SnapAsync(IEnumerable<string> paths, CancellationToken ct)
    {
        var normalized = paths
            .Select(p => TagPathResolver.NormalizeAndBuildCandidatePaths(p).CandidatePaths[^1])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (normalized.Count == 0)
            return new TagPathSnapResult([], [], []);

        var lowered = normalized.Select(p => p.ToLowerInvariant()).ToList();
        var matches = await db.Tags.AsNoTracking()
            .Where(t => lowered.Contains(t.Path.ToLower()))
            .ToListAsync(ct);

        var known = new List<Tag>();
        var unknown = new List<string>();
        foreach (var path in normalized)
        {
            var tag = matches.FirstOrDefault(t => string.Equals(t.Path, path, StringComparison.Ordinal))
                      ?? matches
                          .Where(t => string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase))
                          .OrderBy(t => t.Path, StringComparer.Ordinal)
                          .FirstOrDefault();
            if (tag is null)
                unknown.Add(path);
            else if (known.All(k => k.Id != tag.Id))
                known.Add(tag);
        }

        if (unknown.Count == 0)
            return new TagPathSnapResult(known, [], []);

        // Only on the miss path. A household taxonomy is small (hundreds of rows), so one path-only scan is cheaper
        // than a query per unknown.
        var allPaths = await db.Tags.AsNoTracking().Select(t => t.Path).ToListAsync(ct);
        var suggestions = unknown
            .Select(u => (Unknown: u, Nearest: Nearest(u, allPaths)))
            .Where(s => s.Nearest.Length > 0)
            .ToList();

        return new TagPathSnapResult(known, unknown, suggestions);
    }

    // SnapAsync, but any unknown path is an error the model can act on: what was unknown, what exists nearby, and
    // that inventing a tag needs the user's say-so.
    public async Task<IReadOnlyList<Tag>> RequireKnownAsync(IEnumerable<string> paths, CancellationToken ct)
    {
        var result = await SnapAsync(paths, ct);
        if (result.Unknown.Count == 0)
            return result.Known;

        var message = "Unknown tag path(s): " + string.Join(", ", result.Unknown);
        if (result.Suggestions.Count > 0)
        {
            message += " — nearest: " + (result.Unknown.Count == 1
                ? string.Join(", ", result.Suggestions[0].Nearest)
                : string.Join("; ", result.Suggestions.Select(s => $"{s.Unknown} → {string.Join(", ", s.Nearest)}")));
        }

        throw new McpException(message + ". Use list_tags; call create_tag only if the user asked for a new tag.");
    }

    // Existing paths under the same parent (siblings) or extending the unknown path (it is their case-insensitive
    // prefix), closest first: longest shared prefix, then shortest, then alphabetical.
    private static string[] Nearest(string unknown, IEnumerable<string> existing)
    {
        var parent = ParentOf(unknown);
        return existing
            .Where(p => p.StartsWith(unknown, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(ParentOf(p), parent, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => SharedPrefixLength(p, unknown))
            .ThenBy(p => p.Length)
            .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Take(MaxNearest)
            .ToArray();
    }

    private static string ParentOf(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? "" : path[..slash];
    }

    private static int SharedPrefixLength(string a, string b)
    {
        var i = 0;
        while (i < a.Length && i < b.Length && char.ToLowerInvariant(a[i]) == char.ToLowerInvariant(b[i]))
            i++;
        return i;
    }
}

public sealed record TagPathSnapResult(
    IReadOnlyList<Tag> Known,
    IReadOnlyList<string> Unknown,
    IReadOnlyList<(string Unknown, string[] Nearest)> Suggestions);
