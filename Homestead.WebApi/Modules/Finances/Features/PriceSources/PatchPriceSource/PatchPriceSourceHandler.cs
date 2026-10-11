using System.Text.Json;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.PriceSources.PatchPriceSource;

public class PatchPriceSourceHandler(
    AppDbContext db,
    ICurrentUserAccessor currentUser,
    IAuditContext audit,
    IUnitOfWork unitOfWork)
{
    public async Task<PatchPriceSourceResponse> HandleAsync(
        CommodityPriceSource source, PatchPriceSourceBody body, CancellationToken ct)
    {
        source.RequireExternal("configured");

        var entity = await db.CommodityPriceSourceSettings
            .FirstOrDefaultAsync(s => s.Source == source, ct)
            ?? throw new EntityNotFoundException("Price source settings not found.");

        var before = CommodityPriceSourceSettingsSnapshot.From(entity);

        IReadOnlyList<string> pairAllowList = [];
        if (body.PairAllowList is not null)
        {
            // Persist the normalized (trimmed + de-duplicated) list, not the raw request — a
            // stored entry with surrounding whitespace or a duplicate would otherwise pass
            // validation here yet silently fail to match in the importer's symbol lookup.
            var normalized = await ValidatePairAllowListAsync(body.PairAllowList, ct);
            entity.PairAllowList = JsonSerializer.Serialize(normalized);
            pairAllowList = normalized;
        }

        var now = DateTimeOffset.UtcNow;
        entity.UpdatedAt = now;
        entity.UpdatedByUserId = currentUser.UserId;

        var after = CommodityPriceSourceSettingsSnapshot.From(entity);
        var changes = before.Diff(after);
        if (changes.Count > 0)
        {
            audit.Track(() => changes,
                "CommodityPriceSourceSettings",
                CommodityPriceSourceSettingsSnapshot.SourceToGuid(entity.Source));
        }

        await unitOfWork.SaveChangesAsync(ct);

        return new PatchPriceSourceResponse(entity.Source, pairAllowList);
    }

    // Validates the requested allow-list and returns the normalized (trimmed + de-duplicated)
    // symbols the caller should persist.
    private async Task<List<string>> ValidatePairAllowListAsync(IReadOnlyList<string> symbols, CancellationToken ct)
    {
        var distinct = symbols
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (distinct.Count == 0) return distinct;

        // Resolve against all commodities (currencies and securities) — the importer matches
        // allow-list symbols against every held/known commodity and then filters by provider
        // coverage, so a watched security like VEQT is valid here too.
        var resolved = await db.Commodities
            .Where(c => distinct.Contains(c.Symbol))
            .Select(c => c.Symbol)
            .ToListAsync(ct);

        var resolvedSet = resolved.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = distinct.Where(s => !resolvedSet.Contains(s)).ToList();
        if (missing.Count > 0)
            throw new BusinessRuleException(
                $"Pair allow-list contains unknown symbols: {string.Join(", ", missing)}.");

        return distinct;
    }
}
