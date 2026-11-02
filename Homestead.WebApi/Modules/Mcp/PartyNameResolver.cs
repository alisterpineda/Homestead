using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;

namespace Homestead.WebApi.Modules.Mcp;

// Maps the party names a model sends onto existing parties (party ids never reach the model). Exact name after
// trimming, case-insensitive; an exact-casing match wins when two casings exist (Party.Name's unique index is
// case-sensitive). Read-only: an unknown name is reported, never created.
public sealed class PartyNameResolver(AppDbContext db)
{
    public async Task<PartyNameResolution> ResolveAsync(IEnumerable<string> names, CancellationToken ct)
    {
        var trimmed = names
            .Select(n => n.Trim() is { Length: > 0 } t ? t : throw new ArgumentException("Party name cannot be empty."))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (trimmed.Count == 0)
            return new PartyNameResolution([], []);

        var lowered = trimmed.Select(n => n.ToLowerInvariant()).ToList();
        var matches = await db.Parties.AsNoTracking()
            .Where(p => lowered.Contains(p.Name.ToLower()))
            .Select(p => new ResolvedParty(p.Id, p.Name))
            .ToListAsync(ct);

        var known = new List<ResolvedParty>();
        var unknown = new List<string>();
        foreach (var name in trimmed)
        {
            var party = matches.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal))
                        ?? matches
                            .Where(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                            .OrderBy(p => p.Name, StringComparer.Ordinal)
                            .FirstOrDefault();
            if (party is null)
                unknown.Add(name);
            else if (known.All(k => k.Id != party.Id))
                known.Add(party);
        }

        return new PartyNameResolution(known, unknown);
    }

    // ResolveAsync, but any unknown name is an error the model can act on.
    public async Task<IReadOnlyList<ResolvedParty>> RequireKnownAsync(IEnumerable<string> names, CancellationToken ct)
    {
        var result = await ResolveAsync(names, ct);
        if (result.Unknown.Count == 0)
            return result.Known;

        throw new McpException("Unknown party name(s): " + string.Join(", ", result.Unknown) +
                               ". Use list_parties; call create_party only if the user asked for a new party.");
    }
}

public sealed record ResolvedParty(Guid Id, string Name);

public sealed record PartyNameResolution(IReadOnlyList<ResolvedParty> Known, IReadOnlyList<string> Unknown);
