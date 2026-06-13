namespace Homestead.WebApi.Modules.Inventory.Features.Overview.GetInventoryOverview;

public record GetInventoryOverviewResponse
{
    public required int LowStockCount { get; init; }
    public required IReadOnlyList<LowStockSupplyPreview> LowStockSupplies { get; init; }
    public required int LentCount { get; init; }
    public required IReadOnlyList<LentItemPreview> LentItems { get; init; }
    public required int UnplacedCount { get; init; }
    public required IReadOnlyList<UnplacedItemPreview> UnplacedItems { get; init; }
    public required IReadOnlyList<AreaSummary> Areas { get; init; }
}

public record AreaSummary
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }

    /// <summary>
    /// Resolved count — items resolving here (incl. nested in containers) plus distinct supplies
    /// with stock here; matches what the area-filtered items list shows.
    /// </summary>
    public required int ItemCount { get; init; }

    /// <summary>Distinct low-stock supplies with stock resolving here (same resolved semantics).</summary>
    public required int LowStockCount { get; init; }
}

public record LowStockSupplyPreview
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required decimal TotalOnHand { get; init; }
    public required string Unit { get; init; }
    public required decimal RestockThreshold { get; init; }
}

public record LentItemPreview
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? LentTo { get; init; }
}

public record UnplacedItemPreview
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
}
