using Homestead.WebApi.Modules.Inventory.Features.Items;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Inventory.Services;

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

        // Fast path: when nothing in the batch sits inside a container there is no chain to walk —
        // every item is directly in an area (whose name the projection already carries as AreaName)
        // or unplaced. This is the common case for the single-item GET/create/patch reload, and it
        // avoids both full-table reads below.
        if (items.All(i => i.ParentItemId is null))
            return items.Select(ResolveDirect).ToList();

        // Every valid ancestor is a container. A chain legitimately passes through terminal
        // containers, so this is deliberately NOT filtered by status.
        var containers = await db.Items.AsNoTracking()
            .Where(i => i.IsContainer)
            .Select(i => new ContainerNode(i.Id, i.Name, i.AreaId, i.ParentItemId))
            .ToDictionaryAsync(c => c.Id, ct);

        var areaNames = await db.Areas.AsNoTracking()
            .ToDictionaryAsync(a => a.Id, a => a.Name, ct);

        return items.Select(item => Resolve(item, containers, areaNames)).ToList();
    }

    // Resolves a parent-less item without any DB read: directly-placed items reuse the AreaName the
    // EF projection already joined; unplaced items resolve to an empty path.
    private static ItemResponse ResolveDirect(ItemResponse item) =>
        item.AreaId is { } areaId
            ? item with
            {
                ResolvedAreaId = areaId,
                Path = [new LocationSegmentResponse
                {
                    Id = areaId,
                    Name = item.AreaName ?? string.Empty,
                    Kind = LocationSegmentKind.Area,
                }],
            }
            : item with { ResolvedAreaId = null, Path = [] };

    private static ItemResponse Resolve(
        ItemResponse item,
        IReadOnlyDictionary<Guid, ContainerNode> containers,
        IReadOnlyDictionary<Guid, string> areaNames)
    {
        // Directly placed in an area, or directly unplaced — no container chain to walk.
        if (item.ParentItemId is null)
        {
            if (item.AreaId is { } directAreaId)
                return item with
                {
                    ResolvedAreaId = directAreaId,
                    Path = [AreaSegment(directAreaId, areaNames, item.AreaName)],
                };

            return item with { ResolvedAreaId = null, Path = [] };
        }

        // Walk up the container chain from the immediate parent to its root.
        var chain = new List<LocationSegmentResponse>();
        var visited = new HashSet<Guid> { item.Id }; // never let the item appear in its own path.
        Guid? resolvedAreaId = null;
        LocationSegmentResponse? areaSegment = null;

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
                areaSegment = AreaSegment(chainAreaId, areaNames, null);
                break;
            }

            currentId = node.ParentItemId;
        }

        chain.Reverse(); // root container → immediate parent
        IReadOnlyList<LocationSegmentResponse> path =
            areaSegment is null ? chain : [areaSegment, .. chain];

        return item with { ResolvedAreaId = resolvedAreaId, Path = path };
    }

    private static LocationSegmentResponse AreaSegment(
        Guid areaId, IReadOnlyDictionary<Guid, string> areaNames, string? fallbackName) =>
        new()
        {
            Id = areaId,
            Name = areaNames.GetValueOrDefault(areaId) ?? fallbackName ?? string.Empty,
            Kind = LocationSegmentKind.Area,
        };

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

        var areaNames = await db.Areas.AsNoTracking()
            .ToDictionaryAsync(a => a.Id, a => a.Name, ct);

        var result = new Dictionary<HoldingLocation, LocationPath>(distinct.Count);
        foreach (var location in distinct)
            result[location] = ResolveHolding(location, containers, areaNames);

        return result;
    }

    // Like Resolve, but the holding container is INCLUDED as the path's last segment (stock sits in
    // it, not under it). For an area holding location the path is just that area.
    private static LocationPath ResolveHolding(
        HoldingLocation location,
        IReadOnlyDictionary<Guid, ContainerNode> containers,
        IReadOnlyDictionary<Guid, string> areaNames)
    {
        if (location.AreaId is { } areaId)
            return new LocationPath([AreaSegment(areaId, areaNames, null)]);

        var chain = new List<LocationSegmentResponse>();
        var visited = new HashSet<Guid>();
        LocationSegmentResponse? areaSegment = null;

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
                areaSegment = AreaSegment(chainAreaId, areaNames, null);
                break;
            }

            currentId = node.ParentItemId;
        }

        chain.Reverse(); // root container → holding container
        return new LocationPath(areaSegment is null ? chain : [areaSegment, .. chain]);
    }

    private readonly record struct ContainerNode(Guid Id, string Name, Guid? AreaId, Guid? ParentItemId);
}
