namespace Homestead.WebApi.Modules.Inventory.Features.Brands;

/// <summary>
/// A canonical brand, as returned by the picker's server-search, the Items-list facet, and the Brands
/// management page. Carries identity plus the management-surface fields: how many items reference it
/// (computed at query time — <see cref="Domain.Inventory.Brand"/> has no reverse nav) and its timestamps.
/// </summary>
public record BrandResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }

    /// <summary>Number of items (durables + consumables) attributed to this brand. Gates delete/merge in the UI.</summary>
    public required int ItemCount { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}
