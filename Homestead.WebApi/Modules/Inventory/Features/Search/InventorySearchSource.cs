using System.Globalization;
using Homestead.WebApi.Common.Search;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Features.Search;

/// <summary>
/// Universal-search source for inventory items. Matches an item's own text (Name + Serial + Unit +
/// Compartment + Notes — single table, no joins) via ILIKE substring, with a pg_trgm
/// <c>word_similarity</c> fuzzy fallback on Name (so "battry" → "battery", "boxs" → "box"). The
/// trigram GIN index on <c>inventory.items.Name</c> backs both the fuzzy path and the Name ILIKE.
/// </summary>
public class InventorySearchSource(AppDbContext db) : ISearchSource
{
    // Threshold matches Documents' SuggestionSimilarityThreshold — calibrated so mid-word typos
    // still surface while unrelated words stay below the noise floor.
    private const double NameSimilarityThreshold = 0.3;

    public string Source => "inventory";
    public string Label => "Inventory";

    public async Task<SearchSourceResult> SearchAsync(string query, int limit, CancellationToken ct)
    {
        var pattern = $"%{query}%";

        // Live items only (excludes Sold/Discarded/Archived); both durables and consumables.
        var filtered = db.Items.AsNoTracking()
            .Where(i => ItemStatusExtensions.LiveStatuses.Contains(i.Status))
            .Where(i =>
                EF.Functions.ILike(i.Name, pattern)
                || (i.Serial != null && EF.Functions.ILike(i.Serial, pattern))
                || (i.Unit != null && EF.Functions.ILike(i.Unit, pattern))
                || (i.Compartment != null && EF.Functions.ILike(i.Compartment, pattern))
                || (i.Notes != null && EF.Functions.ILike(i.Notes, pattern))
                || EF.Functions.TrigramsWordSimilarity(query, i.Name) >= NameSimilarityThreshold);

        // Full (uncapped) match count drives "View all (N)".
        var total = await filtered.CountAsync(ct);
        if (total == 0)
            return new SearchSourceResult(0, []);

        // Per-source ranking: best name match first, then alphabetical. Capped at limit.
        var rows = await filtered
            .OrderByDescending(i => EF.Functions.TrigramsWordSimilarity(query, i.Name))
            .ThenBy(i => i.Name)
            .Take(limit)
            .Select(i => new ItemRow(
                i.Id,
                i.Name,
                i.Fungibility,
                i.Status,
                i.Unit,
                i.RestockThreshold,
                i.StockEntries.Sum(s => s.Quantity),
                i.Area != null ? i.Area.Name : null,
                i.ParentItem != null ? i.ParentItem.Name : null))
            .ToListAsync(ct);

        var hits = rows.Select(ToHit).ToList();
        return new SearchSourceResult(total, hits);
    }

    private SearchHit ToHit(ItemRow r)
    {
        var isDurable = r.Fungibility == Fungibility.NonFungible;
        return new SearchHit(
            Source,
            isDurable ? "durable" : "consumable",
            r.Id.ToString(),
            r.Name,
            isDurable ? DurableSubtitle(r) : ConsumableSubtitle(r),
            isDurable ? DurableBadge(r.Status) : ConsumableBadge(r));
    }

    // Direct location: own area, else containing item, else unplaced.
    private static string DurableSubtitle(ItemRow r) => r.AreaName ?? r.ParentName ?? "Unplaced";

    // On-hand total, with the unit of measure when set (e.g. "12 rolls").
    private static string ConsumableSubtitle(ItemRow r)
    {
        var qty = r.OnHand.ToString("0.######", CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(r.Unit) ? qty : $"{qty} {r.Unit}";
    }

    // Show the status only when it's something other than Active.
    private static string? DurableBadge(ItemStatus status) => status switch
    {
        ItemStatus.Active => null,
        ItemStatus.InRepair => "In repair",
        _ => status.ToString(),
    };

    // "Low" when a restock threshold is set and on-hand is at or below it.
    private static string? ConsumableBadge(ItemRow r) =>
        r.RestockThreshold != null && r.OnHand <= r.RestockThreshold ? "Low" : null;

    private sealed record ItemRow(
        Guid Id,
        string Name,
        Fungibility Fungibility,
        ItemStatus Status,
        string? Unit,
        decimal? RestockThreshold,
        decimal OnHand,
        string? AreaName,
        string? ParentName);
}
