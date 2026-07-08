using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Durables.GetDurables;
using Homestead.WebApi.Modules.Inventory.Services;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Features.Durables;

public class GetDurablesHandlerTests : IAsyncLifetime
{
    private TestAppDbContext _db = null!;
    private GetDurablesHandler _sut = null!;
    private Area _garage = null!;
    private Area _office = null!;

    public async ValueTask InitializeAsync()
    {
        _db = TestAppDbContext.Create();
        _garage = new Area { Id = Guid.NewGuid(), Name = "Garage" };
        _office = new Area { Id = Guid.NewGuid(), Name = "Office" };
        _db.Areas.AddRange(_garage, _office);
        await _db.SaveChangesAsync();
        _sut = new GetDurablesHandler(_db, new ItemLocationResolver(_db));
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Item> AddItemAsync(
        string name, ItemStatus status = ItemStatus.Active, Guid? areaId = null,
        Guid? parentItemId = null, DurableKind durableKind = DurableKind.Plain, Guid? brandId = null)
    {
        var item = new Item
        {
            Id = Guid.NewGuid(),
            Name = name,
            Fungibility = Fungibility.NonFungible,
            Status = status,
            AreaId = areaId,
            ParentItemId = parentItemId,
            DurableKind = durableKind,
            BrandId = brandId,
        };
        _db.Items.Add(item);
        await _db.SaveChangesAsync(Ct);
        return item;
    }

    private async Task<Brand> AddBrandAsync(string name)
    {
        var b = new Brand { Id = Guid.NewGuid(), Name = name };
        _db.Brands.Add(b);
        await _db.SaveChangesAsync(Ct);
        return b;
    }

    [Fact]
    public async Task Handle_FiltersByBrand_NarrowsToBrand()
    {
        var apple = await AddBrandAsync("Apple");
        var sony = await AddBrandAsync("Sony");
        await AddItemAsync("MacBook", brandId: apple.Id);
        await AddItemAsync("Bravia TV", brandId: sony.Id);
        await AddItemAsync("Unbranded drill");

        var resp = await _sut.Handle(new GetDurablesQuery { Page = 1, PageSize = 50, BrandId = apple.Id }, Ct);

        Assert.Single(resp.Items);
        Assert.Equal("MacBook", resp.Items[0].Name);
        Assert.Equal(apple.Id, resp.Items[0].BrandId);
        Assert.Equal("Apple", resp.Items[0].BrandName);
    }

    [Fact]
    public async Task Handle_HidesTerminalItemsByDefault()
    {
        await AddItemAsync("Drill");
        await AddItemAsync("Old TV", ItemStatus.Discarded);
        await AddItemAsync("Sold bike", ItemStatus.Sold);
        await AddItemAsync("Archived box", ItemStatus.Archived);

        var resp = await _sut.Handle(new GetDurablesQuery { Page = 1, PageSize = 50 }, Ct);

        Assert.Single(resp.Items);
        Assert.Equal("Drill", resp.Items[0].Name);
    }

    [Fact]
    public async Task Handle_IncludeInactive_ShowsTerminalItems()
    {
        await AddItemAsync("Drill");
        await AddItemAsync("Old TV", ItemStatus.Discarded);

        var resp = await _sut.Handle(new GetDurablesQuery { Page = 1, PageSize = 50, IncludeInactive = true }, Ct);

        Assert.Equal(2, resp.Items.Count);
    }

    [Fact]
    public async Task Handle_FiltersByArea_MatchesDirectlyPlaced()
    {
        await AddItemAsync("Drill", areaId: _garage.Id);
        await AddItemAsync("Stapler", areaId: _office.Id);

        var resp = await _sut.Handle(new GetDurablesQuery { Page = 1, PageSize = 50, AreaId = _garage.Id }, Ct);

        Assert.Single(resp.Items);
        Assert.Equal("Drill", resp.Items[0].Name);
        Assert.Equal("Garage", resp.Items[0].AreaName);
    }

    [Fact]
    public async Task Handle_FiltersByArea_MatchesResolvedNotJustDirect()
    {
        // Wrench nested in a chest that sits in the garage — resolves to garage, not directly placed.
        var chest = await AddItemAsync("Tool chest", areaId: _garage.Id, durableKind: DurableKind.Container);
        await AddItemAsync("Wrench", parentItemId: chest.Id);
        await AddItemAsync("Stapler", areaId: _office.Id);

        var resp = await _sut.Handle(new GetDurablesQuery { Page = 1, PageSize = 50, AreaId = _garage.Id }, Ct);

        // Both the chest (direct) and the nested wrench (resolved) match; the office stapler does not.
        Assert.Equal(2, resp.Items.Count);
        Assert.Contains(resp.Items, i => i.Name == "Wrench");
        Assert.Contains(resp.Items, i => i.Name == "Tool chest");
        Assert.DoesNotContain(resp.Items, i => i.Name == "Stapler");
    }

    [Fact]
    public async Task Handle_FiltersByArea_ExpandsToDescendantAreas()
    {
        var bench = new Area { Id = Guid.NewGuid(), Name = "Workbench", ParentAreaId = _garage.Id };
        _db.Areas.Add(bench);
        await _db.SaveChangesAsync(Ct);
        await AddItemAsync("Vise", areaId: bench.Id); // in a sub-area of the garage
        await AddItemAsync("Stapler", areaId: _office.Id);

        var resp = await _sut.Handle(new GetDurablesQuery { Page = 1, PageSize = 50, AreaId = _garage.Id }, Ct);

        // Filtering the parent area rolls in items resolved to its descendant areas.
        Assert.Single(resp.Items);
        Assert.Equal("Vise", resp.Items[0].Name);
    }

    [Fact]
    public async Task Handle_FiltersUnplaced_MatchesDirectlyUnplaced()
    {
        await AddItemAsync("Drill", areaId: _garage.Id);
        await AddItemAsync("Floating thing");

        var resp = await _sut.Handle(new GetDurablesQuery { Page = 1, PageSize = 50, Unplaced = true }, Ct);

        Assert.Single(resp.Items);
        Assert.Equal("Floating thing", resp.Items[0].Name);
        Assert.Null(resp.Items[0].AreaId);
    }

    [Fact]
    public async Task Handle_FiltersUnplaced_MatchesBrokenContainerChain()
    {
        // A container with no area holding a screw → the screw resolves to no area (unplaced).
        var floatingBin = await AddItemAsync("Floating bin", durableKind: DurableKind.Container);
        await AddItemAsync("Screw", parentItemId: floatingBin.Id);
        await AddItemAsync("Drill", areaId: _garage.Id);

        var resp = await _sut.Handle(new GetDurablesQuery { Page = 1, PageSize = 50, Unplaced = true }, Ct);

        // Both the unplaced bin and the screw nested inside it resolve to null.
        Assert.Equal(2, resp.Items.Count);
        Assert.Contains(resp.Items, i => i.Name == "Floating bin");
        Assert.Contains(resp.Items, i => i.Name == "Screw");
        Assert.DoesNotContain(resp.Items, i => i.Name == "Drill");
    }

    [Fact]
    public async Task Handle_FiltersByArea_TotalCountReflectsFilteredSet_NotPageLocal()
    {
        // Seed more matching items than one page so TotalCount can't be the page-local count.
        for (var i = 0; i < 5; i++)
            await AddItemAsync($"Garage item {i:D2}", areaId: _garage.Id);
        await AddItemAsync("Office stapler", areaId: _office.Id);

        var resp = await _sut.Handle(new GetDurablesQuery { Page = 1, PageSize = 2, AreaId = _garage.Id }, Ct);

        Assert.Equal(2, resp.Items.Count); // one page
        Assert.Equal(5, resp.TotalCount);  // whole filtered set, not the page or the office item
    }

    [Fact]
    public async Task Handle_FiltersBySearch()
    {
        await AddItemAsync("Cordless drill");
        await AddItemAsync("Stapler");

        var resp = await _sut.Handle(new GetDurablesQuery { Page = 1, PageSize = 50, Search = "drill" }, Ct);

        Assert.Single(resp.Items);
        Assert.Equal("Cordless drill", resp.Items[0].Name);
    }

    [Fact]
    public async Task Handle_FiltersByParentItemId_ReturnsDirectChildrenOnly()
    {
        var chest = await AddItemAsync("Tool chest", areaId: _garage.Id, durableKind: DurableKind.Container);
        var bin = await AddItemAsync("Bin", parentItemId: chest.Id, durableKind: DurableKind.Container);
        await AddItemAsync("Wrench", parentItemId: chest.Id); // direct child
        await AddItemAsync("Screw driver", parentItemId: bin.Id); // grandchild

        var resp = await _sut.Handle(
            new GetDurablesQuery { Page = 1, PageSize = 50, ParentItemId = chest.Id }, Ct);

        Assert.Equal(2, resp.Items.Count); // bin + wrench, not the grandchild
        Assert.Contains(resp.Items, i => i.Name == "Bin");
        Assert.Contains(resp.Items, i => i.Name == "Wrench");
        Assert.DoesNotContain(resp.Items, i => i.Name == "Screw driver");
    }

    [Fact]
    public async Task Handle_FiltersByDurableKinds_ContainerAndAssembly()
    {
        await AddItemAsync("Tool chest", areaId: _garage.Id, durableKind: DurableKind.Container);
        await AddItemAsync("Gaming PC", areaId: _garage.Id, durableKind: DurableKind.Assembly);
        await AddItemAsync("Wrench", areaId: _garage.Id);

        var resp = await _sut.Handle(
            new GetDurablesQuery
            {
                Page = 1,
                PageSize = 50,
                DurableKinds = [DurableKind.Container, DurableKind.Assembly],
            }, Ct);

        Assert.Equal(2, resp.Items.Count);
        Assert.Contains(resp.Items, i => i.Name == "Tool chest");
        Assert.Contains(resp.Items, i => i.Name == "Gaming PC");
    }

    [Fact]
    public async Task Handle_FiltersByDurableKinds_SingleKind()
    {
        await AddItemAsync("Tool chest", areaId: _garage.Id, durableKind: DurableKind.Container);
        await AddItemAsync("Gaming PC", areaId: _garage.Id, durableKind: DurableKind.Assembly);

        var resp = await _sut.Handle(
            new GetDurablesQuery { Page = 1, PageSize = 50, DurableKinds = [DurableKind.Container] }, Ct);

        Assert.Single(resp.Items);
        Assert.Equal("Tool chest", resp.Items[0].Name);
    }

    [Fact]
    public async Task Handle_NoKindFilter_IncludesComponentsAndAll()
    {
        var pc = await AddItemAsync("Gaming PC", areaId: _garage.Id, durableKind: DurableKind.Assembly);
        await AddItemAsync("CPU", parentItemId: pc.Id); // a component is first-class in the list

        var resp = await _sut.Handle(new GetDurablesQuery { Page = 1, PageSize = 50 }, Ct);

        Assert.Equal(2, resp.Items.Count);
        Assert.Contains(resp.Items, i => i.Name == "CPU");
    }
}
