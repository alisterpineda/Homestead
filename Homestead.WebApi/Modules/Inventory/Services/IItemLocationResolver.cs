using Homestead.WebApi.Modules.Inventory.Features.Items;

namespace Homestead.WebApi.Modules.Inventory.Services;

/// <summary>
/// Fills the derived <see cref="ItemResponse.Path"/> / <see cref="ItemResponse.ResolvedAreaId"/> on
/// already-projected item responses by walking the container chain. Resolved location is never
/// stored (see <c>locations.md</c>) — it is computed here at read time.
/// </summary>
public interface IItemLocationResolver
{
    /// <summary>Enriches a batch of responses, fetching containers and area names once for the whole set.</summary>
    Task<IReadOnlyList<ItemResponse>> EnrichAsync(IReadOnlyList<ItemResponse> items, CancellationToken ct = default);

    /// <summary>Enriches a single response.</summary>
    Task<ItemResponse> EnrichAsync(ItemResponse item, CancellationToken ct = default);

    /// <summary>
    /// Resolves a set of stock holding locations (an area, or a container) into display paths,
    /// root→holding-location with the holding area/container as the last segment. The keys are the
    /// distinct non-empty locations of the input; a fully-null (deleted) location is skipped and a
    /// broken chain yields the path resolved so far. Used by the supplies breakdown and the ledger.
    /// </summary>
    Task<IReadOnlyDictionary<HoldingLocation, LocationPath>> ResolveHoldingLocationsAsync(
        IReadOnlyCollection<HoldingLocation> locations, CancellationToken ct = default);
}

/// <summary>A place stock can sit: an area, or a container item. Both null means "deleted/unknown".</summary>
public readonly record struct HoldingLocation(Guid? AreaId, Guid? ContainerItemId)
{
    public bool IsEmpty => AreaId is null && ContainerItemId is null;
}

/// <summary>A resolved holding-location path plus its joined display label.</summary>
public sealed record LocationPath(IReadOnlyList<LocationSegmentResponse> Segments)
{
    public static readonly LocationPath Empty = new([]);

    /// <summary>Joined breadcrumb, e.g. <c>Garage › Cupboard › Bin</c>; null when unresolved.</summary>
    public string? Label => Segments.Count == 0 ? null : string.Join(" › ", Segments.Select(s => s.Name));
}
