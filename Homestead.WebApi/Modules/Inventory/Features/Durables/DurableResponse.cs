using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Items;

namespace Homestead.WebApi.Modules.Inventory.Features.Durables;

public record DurableResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required Fungibility Fungibility { get; init; }
    public required ItemStatus Status { get; init; }
    public string? Notes { get; init; }

    public Guid? AreaId { get; init; }
    public string? AreaName { get; init; }
    public Guid? ParentItemId { get; init; }

    /// <summary>Whether this item is a storage container; null for a fungible consumable.</summary>
    public bool? IsContainer { get; init; }

    /// <summary>
    /// The immediate parent's <see cref="IsContainer"/>; null when the item has no parent. Drives the
    /// client's "Part of X" vs "In X" label/icon without a second fetch — a non-container parent
    /// (<c>false</c>) means this item is a component.
    /// </summary>
    public bool? ParentIsContainer { get; init; }

    /// <summary>
    /// Whether this item has ≥1 live child (derived, never stored). The client reads it two ways:
    /// gated by <see cref="IsContainer"/> it means "non-container assembly" (component children) —
    /// driving the assembly icon and the picker's "Part of" group; ungated it means "holds something
    /// live", which the edit sheet uses to disable the container toggle (a populated item can't be
    /// flipped either direction). It is <c>true</c> for a container with live stored contents too.
    /// </summary>
    public bool HasComponents { get; init; }

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

    /// <summary>Canonical brand id, or null when unattributed.</summary>
    public Guid? BrandId { get; init; }

    /// <summary>Brand name, denormalised for display (like <see cref="ValueCurrencySymbol"/>); null when unattributed.</summary>
    public string? BrandName { get; init; }

    public string? Serial { get; init; }

    /// <summary>Free-text model designation; durable-only.</summary>
    public string? Model { get; init; }

    public DateOnly? AcquiredOn { get; init; }

    /// <summary>How the item entered the household; null = not recorded.</summary>
    public AcquisitionKind? AcquisitionKind { get; init; }

    /// <summary>Purchase price (historical fact, distinct from the estimated <see cref="Value"/>).</summary>
    public decimal? AcquisitionCost { get; init; }
    public Guid? AcquisitionCostCurrencyId { get; init; }
    public string? AcquisitionCostCurrencySymbol { get; init; }

    /// <summary>Free-text source — a store or a person.</summary>
    public string? AcquiredFrom { get; init; }

    public string? LentTo { get; init; }

    public string? Unit { get; init; }
    public decimal? RestockThreshold { get; init; }

    /// <summary>Hierarchical classification tags on this item, each with resolved effective colour.</summary>
    public IReadOnlyList<ItemTagDto> Tags { get; init; } = [];

    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}
