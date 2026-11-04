using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;

namespace Homestead.WebApi.Modules.Mcp;

// Maps the party names a model sends onto existing parties (party ids never reach the model). Exact name after
// trimming, case-insensitive; an exact-casing match wins when two casings exist (Party.Name's unique index is
// case-sensitive). Read-only: an unknown name is reported with the nearest existing names (McpNearest), never created.
public sealed class PartyNameResolver(AppDbContext db)
{
    public async Task<PartyNameResolution> ResolveAsync(IEnumerable<string> names, CancellationToken ct)
    {
        var trimmed = names.Select(NormalizeName).ToList();
        if (trimmed.Count == 0)
            return new PartyNameResolution([], []);

        var matches = await db.Parties.AsNoTracking()
            .WhereNameIn(p => p.Name, trimmed)
            .Select(p => new ResolvedParty(p.Id, p.Name))
            .ToListAsync(ct);

        var (known, unknown) = CaseInsensitiveMatch.Choose(trimmed, matches, p => p.Id, p => p.Name);
        return new PartyNameResolution(known, unknown);
    }

    // The one trim of a party name the model sends, for additions and removals alike; a blank name is invalid input.
    public static string NormalizeName(string name) =>
        name.Trim() is { Length: > 0 } trimmed ? trimmed : throw new ArgumentException("Party name cannot be empty.");

    // ResolveAsync, but any unknown name is an error the model can act on: what was unknown, what exists nearby, and
    // that inventing a party needs the user's say-so.
    public async Task<IReadOnlyList<ResolvedParty>> RequireKnownAsync(IEnumerable<string> names, CancellationToken ct)
    {
        var result = await ResolveAsync(names, ct);
        if (result.Unknown.Count == 0)
            return result.Known;

        // Only on the miss path; a household has at most a few hundred parties.
        var allNames = await db.Parties.AsNoTracking().Select(p => p.Name).ToListAsync(ct);
        var suggestions = result.Unknown
            .Select(u => (Unknown: u, Nearest: McpNearest.Find(u, allNames)))
            .Where(s => s.Nearest.Length > 0)
            .ToList();

        throw new McpException(McpNearest.Describe("party name(s)", result.Unknown, suggestions) +
                               ". Use list_parties; call create_party only if the user asked for a new party.");
    }
}

public sealed record ResolvedParty(Guid Id, string Name);

public sealed record PartyNameResolution(IReadOnlyList<ResolvedParty> Known, IReadOnlyList<string> Unknown);
