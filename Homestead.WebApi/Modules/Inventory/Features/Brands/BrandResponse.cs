namespace Homestead.WebApi.Modules.Inventory.Features.Brands;

/// <summary>
/// A canonical brand, as returned by the picker's server-search and the Items-list facet. v1 carries
/// only identity — no item count (deferred).
/// </summary>
public record BrandResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
}
