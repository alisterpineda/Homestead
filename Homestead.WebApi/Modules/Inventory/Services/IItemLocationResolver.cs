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
}
