using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Items;
using Homestead.WebApi.Modules.Inventory.Services;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Services;

public class ItemLocationResolverTests : IAsyncLifetime
{
    private TestAppDbContext _db = null!;
    private ItemLocationResolver _sut = null!;

    public ValueTask InitializeAsync()
    {
        _db = TestAppDbContext.Create();
        _sut = new ItemLocationResolver(_db);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Area> AddAreaAsync(string name, Guid? parentAreaId = null)
    {
        var area = new Area { Id = Guid.NewGuid(), Name = name, ParentAreaId = parentAreaId };
        _db.Areas.Add(area);
        await _db.SaveChangesAsync(Ct);
        return area;
    }

    private async Task<Item> AddContainerAsync(string name, Guid? areaId = null, Guid? parentItemId = null)
    {
        var item = new Item
        {
            Id = Guid.NewGuid(),
            Name = name,
            Fungibility = Fungibility.NonFungible,
            Status = ItemStatus.Active,
            IsContainer = true,
            AreaId = areaId,
            ParentItemId = parentItemId,
        };
        _db.Items.Add(item);
        await _db.SaveChangesAsync(Ct);
        return item;
    }

    private static ItemResponse Response(Guid id, Guid? areaId = null, string? areaName = null, Guid? parentItemId = null) => new()
    {
        Id = id,
        Name = "Item",
        Fungibility = Fungibility.NonFungible,
        Status = ItemStatus.Active,
        IsContainer = false,
        AreaId = areaId,
        AreaName = areaName,
        ParentItemId = parentItemId,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task Enrich_DirectlyPlacedInNestedArea_PathIsFullAncestorChainRootToLeaf()
    {
        var house = await AddAreaAsync("House");
        var basement = await AddAreaAsync("Basement", parentAreaId: house.Id);
        var bedroom = await AddAreaAsync("Bedroom", parentAreaId: basement.Id);

        var enriched = await _sut.EnrichAsync(Response(Guid.NewGuid(), areaId: bedroom.Id, areaName: "Bedroom"), Ct);

        Assert.Equal(bedroom.Id, enriched.ResolvedAreaId); // leaf is unchanged
        Assert.Equal(["House", "Basement", "Bedroom"], enriched.Path.Select(s => s.Name));
        Assert.All(enriched.Path, s => Assert.Equal(LocationSegmentKind.Area, s.Kind));
    }

    [Fact]
    public async Task Enrich_NestedInContainerInNestedArea_PathIsAreaAncestorsThenContainers()
    {
        var house = await AddAreaAsync("House");
        var bedroom = await AddAreaAsync("Bedroom", parentAreaId: house.Id);
        var chest = await AddContainerAsync("Chest", areaId: bedroom.Id);
        var bin = await AddContainerAsync("Bin", parentItemId: chest.Id);

        var enriched = await _sut.EnrichAsync(Response(Guid.NewGuid(), parentItemId: bin.Id), Ct);

        Assert.Equal(bedroom.Id, enriched.ResolvedAreaId);
        // Area ancestors (root→leaf) then the container chain (root→immediate parent).
        Assert.Equal(["House", "Bedroom", "Chest", "Bin"], enriched.Path.Select(s => s.Name));
        Assert.Equal(
            [LocationSegmentKind.Area, LocationSegmentKind.Area, LocationSegmentKind.Container, LocationSegmentKind.Container],
            enriched.Path.Select(s => s.Kind));
    }

    [Fact]
    public async Task Enrich_RootArea_PathIsSingleSegment()
    {
        var house = await AddAreaAsync("House");

        var enriched = await _sut.EnrichAsync(Response(Guid.NewGuid(), areaId: house.Id, areaName: "House"), Ct);

        Assert.Equal(house.Id, enriched.ResolvedAreaId);
        Assert.Equal(["House"], enriched.Path.Select(s => s.Name));
    }

    [Fact]
    public async Task Enrich_AreaParentCycle_DoesNotLoop()
    {
        var house = await AddAreaAsync("House");
        var basement = await AddAreaAsync("Basement", parentAreaId: house.Id);
        house.ParentAreaId = basement.Id; // cycle
        await _db.SaveChangesAsync(Ct);

        var enriched = await _sut.EnrichAsync(Response(Guid.NewGuid(), areaId: basement.Id, areaName: "Basement"), Ct);

        // Cycle-guarded: each area appears at most once, so the path is finite.
        Assert.Equal(2, enriched.Path.Count);
        Assert.Equal(enriched.Path.Select(s => s.Id).Distinct().Count(), enriched.Path.Count);
        Assert.Equal(basement.Id, enriched.ResolvedAreaId);
    }

    [Fact]
    public async Task ResolveAreaPath_ReturnsFullAncestorChainRootToLeaf()
    {
        var house = await AddAreaAsync("House");
        var basement = await AddAreaAsync("Basement", parentAreaId: house.Id);
        var bedroom = await AddAreaAsync("Bedroom", parentAreaId: basement.Id);

        var path = await _sut.ResolveAreaPathAsync(bedroom.Id, Ct);

        Assert.Equal(["House", "Basement", "Bedroom"], path.Segments.Select(s => s.Name));
        Assert.Equal("House › Basement › Bedroom", path.Label);
    }
}
