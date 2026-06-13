using Homestead.WebApi.Modules.Inventory.Features.Items;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Services;

/// <summary>
/// <see cref="InventoryPlacementService"/> duplicates this walk's chain semantics (unfiltered
/// ancestor statuses; broken or cyclic chain ⇒ unplaced; area-parent climb for the ancestor path)
/// for aggregate area counts — any change to the rules here must be mirrored there, or counts will
/// disagree with per-item resolution.
/// </summary>
public class ItemLocationResolver(AppDbContext db) : IItemLocationResolver
{
    public async Task<ItemResponse> EnrichAsync(ItemResponse item, CancellationToken ct = default)
    {
        var enriched = await EnrichAsync(new[] { item }, ct);
        return enriched[0];
    }

    public async Task<IReadOnlyList<ItemResponse>> EnrichAsync(
        IReadOnlyList<ItemResponse> items, CancellationToken ct = default)
    {
        if (items.Count == 0)
            return items;

        // The leaf area's ancestor chain (House › Basement › Bedroom) is needed for every placed
        // item, so the (small, household-scale) areas table is always loaded — even on the fast
        // path below. The larger containers table is only loaded when something is nested.
        var areaNodes = await LoadAreaNodesAsync(ct);

        // Fast path: when nothing in the batch sits inside a container there is no container chain
        // to walk — every item is directly in an area or unplaced. This is the common case for the
        // single-item GET/create/patch reload, and it avoids the larger containers read below.
        if (items.All(i => i.ParentItemId is null))
            return items.Select(i => ResolveDirect(i, areaNodes)).ToList();

        // Every valid ancestor is a container. A chain legitimately passes through terminal
        // containers, so this is deliberately NOT filtered by status.
        var containers = await db.Items.AsNoTracking()
            .Where(i => i.IsContainer)
            .Select(i => new ContainerNode(i.Id, i.Name, i.AreaId, i.ParentItemId))
            .ToDictionaryAsync(c => c.Id, ct);

        return items.Select(item => Resolve(item, containers, areaNodes)).ToList();
    }

    public async Task<LocationPath> ResolveAreaPathAsync(Guid areaId, CancellationToken ct = default)
    {
        var areaNodes = await LoadAreaNodesAsync(ct);
        return new LocationPath(BuildAreaSegments(areaId, areaNodes, fallbackName: null));
    }

    private async Task<Dictionary<Guid, AreaNode>> LoadAreaNodesAsync(CancellationToken ct) =>
        await db.Areas.AsNoTracking()
            .Select(a => new { a.Id, a.Name, a.ParentAreaId })
            .ToDictionaryAsync(a => a.Id, a => new AreaNode(a.Name, a.ParentAreaId), ct);

    // Resolves a parent-less item without a container read: a directly-placed item gets the full
    // area-ancestor path for its area; an unplaced item resolves to an empty path.
    private static ItemResponse ResolveDirect(
        ItemResponse item, IReadOnlyDictionary<Guid, AreaNode> areaNodes) =>
        item.AreaId is { } areaId
            ? item with
            {
                ResolvedAreaId = areaId,
                Path = BuildAreaSegments(areaId, areaNodes, item.AreaName),
            }
            : item with { ResolvedAreaId = null, Path = [] };

    private static ItemResponse Resolve(
        ItemResponse item,
        IReadOnlyDictionary<Guid, ContainerNode> containers,
        IReadOnlyDictionary<Guid, AreaNode> areaNodes)
    {
        // Directly placed in an area, or directly unplaced — no container chain to walk.
        if (item.ParentItemId is null)
        {
            if (item.AreaId is { } directAreaId)
                return item with
                {
                    ResolvedAreaId = directAreaId,
                    Path = BuildAreaSegments(directAreaId, areaNodes, item.AreaName),
                };

            return item with { ResolvedAreaId = null, Path = [] };
        }

        // Walk up the container chain from the immediate parent to its root.
        var chain = new List<LocationSegmentResponse>();
        var visited = new HashSet<Guid> { item.Id }; // never let the item appear in its own path.
        Guid? resolvedAreaId = null;

        var currentId = item.ParentItemId;
        while (currentId is { } id)
        {
            // Broken or cyclic chain → resolved-Unplaced from this point.
            if (!visited.Add(id) || !containers.TryGetValue(id, out var node))
                break;

            chain.Add(new LocationSegmentResponse
            {
                Id = node.Id,
                Name = node.Name,
                Kind = LocationSegmentKind.Container,
            });

            if (node.AreaId is { } chainAreaId)
            {
                resolvedAreaId = chainAreaId;
                break;
            }

            currentId = node.ParentItemId;
        }

        chain.Reverse(); // root container → immediate parent
        IReadOnlyList<LocationSegmentResponse> path = resolvedAreaId is { } leaf
            ? [.. BuildAreaSegments(leaf, areaNodes, fallbackName: null), .. chain]
            : chain;

        return item with { ResolvedAreaId = resolvedAreaId, Path = path };
    }

    // Walks the area parent chain from the leaf up to its root, cycle-guarded, and returns the area
    // segments ordered root→leaf (House › Basement › Bedroom). Mirrors the container-chain climb.
    private static IReadOnlyList<LocationSegmentResponse> BuildAreaSegments(
        Guid leafAreaId, IReadOnlyDictionary<Guid, AreaNode> areaNodes, string? fallbackName)
    {
        var segments = new List<LocationSegmentResponse>();
        var visited = new HashSet<Guid>();

        Guid? currentId = leafAreaId;
        while (currentId is { } id && visited.Add(id) && areaNodes.TryGetValue(id, out var node))
        {
            segments.Add(new LocationSegmentResponse
            {
                Id = id,
                Name = node.Name,
                Kind = LocationSegmentKind.Area,
            });
            currentId = node.ParentAreaId;
        }

        // Defensive: a leaf id missing from the map (shouldn't happen — area FKs SetNull on delete)
        // still renders one segment using the projection's joined name, matching prior behaviour.
        if (segments.Count == 0)
            segments.Add(new LocationSegmentResponse
            {
                Id = leafAreaId,
                Name = fallbackName ?? string.Empty,
                Kind = LocationSegmentKind.Area,
            });

        segments.Reverse(); // root → leaf
        return segments;
    }

    public async Task<IReadOnlyDictionary<HoldingLocation, LocationPath>> ResolveHoldingLocationsAsync(
        IReadOnlyCollection<HoldingLocation> locations, CancellationToken ct = default)
    {
        var distinct = locations.Where(l => !l.IsEmpty).Distinct().ToList();
        if (distinct.Count == 0)
            return new Dictionary<HoldingLocation, LocationPath>();

        // The container ids referenced directly as holding locations — loaded by id as well as by
        // role so a container whose role was switched off after a historical movement still shows
        // its own name (its ancestors may then break the chain, which renders gracefully).
        var referencedContainerIds = distinct
            .Where(l => l.ContainerItemId is not null)
            .Select(l => l.ContainerItemId!.Value)
            .ToHashSet();

        var containers = await db.Items.AsNoTracking()
            .Where(i => i.IsContainer || referencedContainerIds.Contains(i.Id))
            .Select(i => new ContainerNode(i.Id, i.Name, i.AreaId, i.ParentItemId))
            .ToDictionaryAsync(c => c.Id, ct);

        var areaNodes = await LoadAreaNodesAsync(ct);

        var result = new Dictionary<HoldingLocation, LocationPath>(distinct.Count);
        foreach (var location in distinct)
            result[location] = ResolveHolding(location, containers, areaNodes);

        return result;
    }

    // Like Resolve, but the holding container is INCLUDED as the path's last segment (stock sits in
    // it, not under it). For an area holding location the path is the area's full ancestor chain.
    private static LocationPath ResolveHolding(
        HoldingLocation location,
        IReadOnlyDictionary<Guid, ContainerNode> containers,
        IReadOnlyDictionary<Guid, AreaNode> areaNodes)
    {
        if (location.AreaId is { } areaId)
            return new LocationPath(BuildAreaSegments(areaId, areaNodes, fallbackName: null));

        var chain = new List<LocationSegmentResponse>();
        var visited = new HashSet<Guid>();
        Guid? resolvedAreaId = null;

        var currentId = location.ContainerItemId;
        while (currentId is { } id)
        {
            if (!visited.Add(id) || !containers.TryGetValue(id, out var node))
                break;

            chain.Add(new LocationSegmentResponse
            {
                Id = node.Id,
                Name = node.Name,
                Kind = LocationSegmentKind.Container,
            });

            if (node.AreaId is { } chainAreaId)
            {
                resolvedAreaId = chainAreaId;
                break;
            }

            currentId = node.ParentItemId;
        }

        chain.Reverse(); // root container → holding container
        return new LocationPath(resolvedAreaId is { } leaf
            ? [.. BuildAreaSegments(leaf, areaNodes, fallbackName: null), .. chain]
            : chain);
    }

    private readonly record struct ContainerNode(Guid Id, string Name, Guid? AreaId, Guid? ParentItemId);

    private readonly record struct AreaNode(string Name, Guid? ParentAreaId);
}
