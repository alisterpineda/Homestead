namespace Homestead.WebApi.Modules.Inventory.Features.Overview.GetInventoryOverview;

public record GetInventoryOverviewResponse
{
    public required int LowStockCount { get; init; }
    public required IReadOnlyList<LowStockConsumablePreview> LowStockConsumables { get; init; }
    public required int LentCount { get; init; }
    public required IReadOnlyList<LentDurablePreview> LentDurables { get; init; }
    public required int UnplacedCount { get; init; }
    public required IReadOnlyList<UnplacedDurablePreview> UnplacedDurables { get; init; }
    public required IReadOnlyList<AreaSummary> Areas { get; init; }
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

public record UnplacedDurablePreview
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
}
