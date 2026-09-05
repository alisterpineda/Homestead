using Homestead.WebApi.Modules.Inventory.Features.Consumables;
using Homestead.WebApi.Modules.Inventory.Features.Durables;

namespace Homestead.WebApi.Modules.Inventory.Features.Overview.GetInventoryOverview;

public record GetInventoryOverviewResponse
{
    public required int LowStockCount { get; init; }
    public required IReadOnlyList<LowStockConsumablePreview> LowStockConsumables { get; init; }
    public required int LentCount { get; init; }
    public required IReadOnlyList<LentDurablePreview> LentDurables { get; init; }
    public required int UnplacedCount { get; init; }

    /// <summary>
    /// Live durables whose warranty cover lapses within
    /// <see cref="Homestead.WebApi.Domain.Inventory.WarrantyStatusRules.ExpiringWindowDays"/> days —
    /// i.e. whose derived status is <c>Expiring</c>.
    /// </summary>
    public required int ExpiringWarrantyCount { get; init; }

    /// <summary>Most-urgent-first preview of <see cref="ExpiringWarrantyCount"/>, capped at five.</summary>
    public required IReadOnlyList<ExpiringWarrantyPreview> ExpiringWarranties { get; init; }

    public required IReadOnlyList<AreaSummary> Areas { get; init; }

    /// <summary>
    /// The most-recently-touched live items (durables + consumables, mixed), newest first, for the
    /// browse-root "Recently updated" strip. Recency is the effective last-activity time — the later
    /// of <c>Item.UpdatedAt</c> and, for a consumable, its latest stock entry's <c>UpdatedAt</c>.
    /// </summary>
    public required IReadOnlyList<RecentlyUpdatedItem> RecentlyUpdated { get; init; }
}

/// <summary>
/// One entry in the "Recently updated" strip: a full, location-enriched durable or consumable DTO
/// (so a chip click can open the quick panel with no re-fetch) tagged with its effective
/// last-activity time. Exactly one of <see cref="Durable"/> / <see cref="Consumable"/> is set.
/// </summary>
public record RecentlyUpdatedItem
{
    public required DateTimeOffset LastActivityAt { get; init; }
    public DurableResponse? Durable { get; init; }
    public ConsumableResponse? Consumable { get; init; }
}

public record AreaSummary
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }

    /// <summary>Parent area, or null for a top-level area. The browse root grid shows roots only.</summary>
    public Guid? ParentAreaId { get; init; }

    /// <summary>
    /// Resolved count — items resolving here (incl. nested in containers) plus distinct consumables
    /// with stock here; matches what the area-filtered items list shows.
    /// </summary>
    public required int ItemCount { get; init; }

    /// <summary>Distinct low-stock consumables with stock resolving here (same resolved semantics).</summary>
    public required int LowStockCount { get; init; }
}

public record LowStockConsumablePreview
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required decimal TotalOnHand { get; init; }
    public string? Unit { get; init; }
    public required decimal RestockThreshold { get; init; }
}

public record LentDurablePreview
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? LentTo { get; init; }
}

/// <summary>
/// One row of the "Warranty expiring" tile. <see cref="ExpiresOn"/> is the item's <b>latest</b>
/// expiry — the date its cover actually runs out (warranties.md §Derived status).
/// </summary>
public record ExpiringWarrantyPreview(Guid Id, string Name, DateOnly ExpiresOn);
