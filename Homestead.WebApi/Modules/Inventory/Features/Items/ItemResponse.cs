using Homestead.WebApi.Domain.Inventory;

namespace Homestead.WebApi.Modules.Inventory.Features.Items;

public record ItemResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required Fungibility Fungibility { get; init; }
    public required ItemStatus Status { get; init; }
    public string? Notes { get; init; }

    public Guid? AreaId { get; init; }
    public string? AreaName { get; init; }
    public Guid? ParentItemId { get; init; }
    public required bool IsContainer { get; init; }
    public string? Compartment { get; init; }

    /// <summary>
    /// Resolved location chain root→immediate-parent, never including the item itself. The first
    /// segment is the resolving <see cref="LocationSegmentKind.Area"/> when the chain reaches one;
    /// a chain rooted at an unplaced container carries no area segment; <c>[]</c> for a directly
    /// unplaced item. The immediate parent's name is <c>Path[^1]</c>. Filled by the resolver
    /// post-query — not part of the EF projection.
    /// </summary>
    public IReadOnlyList<LocationSegmentResponse> Path { get; init; } = [];

    /// <summary>Resolved area id (walk the parent chain to its root area); null = resolved-Unplaced.</summary>
    public Guid? ResolvedAreaId { get; init; }

    public decimal? Value { get; init; }
    public Guid? ValueCurrencyId { get; init; }
    public string? ValueCurrencySymbol { get; init; }

    public string? Serial { get; init; }
    public DateOnly? AcquiredOn { get; init; }
    public string? LentTo { get; init; }

    public string? Unit { get; init; }
    public decimal? RestockThreshold { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}
