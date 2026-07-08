using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Consumables.GetConsumables;
using Homestead.WebApi.Modules.Inventory.Services;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Features.Consumables;

public class GetConsumablesHandlerTests : IAsyncLifetime
{
    private TestAppDbContext _db = null!;
    private GetConsumablesHandler _sut = null!;
    private Area _garage = null!;

    public async ValueTask InitializeAsync()
    {
        _db = TestAppDbContext.Create();
        _garage = new Area { Id = Guid.NewGuid(), Name = "Garage" };
        _db.Areas.Add(_garage);
        await _db.SaveChangesAsync();
        _sut = new GetConsumablesHandler(_db, new ItemLocationResolver(_db));
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Item> AddConsumableAsync(
        string name, ItemStatus status = ItemStatus.Active, decimal? quantity = null,
        decimal? restockThreshold = null)
    {
        var consumable = new Item
        {
            Id = Guid.NewGuid(),
            Name = name,
            Fungibility = Fungibility.Fungible,
            Status = status,
            Unit = "pcs",
            RestockThreshold = restockThreshold,
        };
        _db.Items.Add(consumable);
        if (quantity is { } qty)
        {
            _db.StockEntries.Add(new StockEntry
            {
                Id = Guid.NewGuid(),
                ItemId = consumable.Id,
                AreaId = _garage.Id,
                Quantity = qty,
            });
        }
        await _db.SaveChangesAsync(Ct);
        return consumable;
    }

    private async Task<Item> AddBareConsumableAsync(string name)
    {
        var consumable = new Item
        {
            Id = Guid.NewGuid(), Name = name, Fungibility = Fungibility.Fungible,
            Status = ItemStatus.Active, Unit = "pcs",
        };
        _db.Items.Add(consumable);
        await _db.SaveChangesAsync(Ct);
        return consumable;
    }

    private async Task<Item> AddContainerAsync(string name, Guid? areaId = null, Guid? parentItemId = null)
    {
        var container = new Item
        {
            Id = Guid.NewGuid(), Name = name, Fungibility = Fungibility.NonFungible,
            Status = ItemStatus.Active, DurableKind = DurableKind.Container,
            AreaId = areaId, ParentItemId = parentItemId,
        };
        _db.Items.Add(container);
        await _db.SaveChangesAsync(Ct);
        return container;
    }

    private async Task<Area> AddAreaAsync(string name, Guid? parentAreaId = null)
    {
        var area = new Area { Id = Guid.NewGuid(), Name = name, ParentAreaId = parentAreaId };
        _db.Areas.Add(area);
        await _db.SaveChangesAsync(Ct);
        return area;
    }

    private async Task AddStockAsync(Guid consumableId, decimal qty, Guid? areaId = null, Guid? containerItemId = null)
    {
        _db.StockEntries.Add(new StockEntry
        {
            Id = Guid.NewGuid(), ItemId = consumableId, Quantity = qty,
            AreaId = areaId, ContainerItemId = containerItemId,
        });
        await _db.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task Handle_HidesArchivedConsumablesByDefault()
    {
        await AddConsumableAsync("Wood screws");
        await AddConsumableAsync("Old caulk", ItemStatus.Archived);

        var resp = await _sut.Handle(new GetConsumablesQuery { Page = 1, PageSize = 50 }, Ct);

        Assert.Single(resp.Items);
        Assert.Equal("Wood screws", resp.Items[0].Name);
    }

    [Fact]
    public async Task Handle_FiltersBySearch()
    {
        await AddConsumableAsync("Wood screws");
        await AddConsumableAsync("Paper towels");

        var resp = await _sut.Handle(new GetConsumablesQuery { Page = 1, PageSize = 50, Search = "screw" }, Ct);

        Assert.Single(resp.Items);
        Assert.Equal("Wood screws", resp.Items[0].Name);
    }

    [Fact]
    public async Task Handle_LowStockOnly_ReturnsConsumablesAtOrBelowThreshold()
    {
        await AddConsumableAsync("Wood screws", quantity: 4, restockThreshold: 5); // below
        await AddConsumableAsync("Sandpaper", quantity: 5, restockThreshold: 5); // exactly at — still low
        await AddConsumableAsync("Tape", quantity: 10, restockThreshold: 2); // above
        await AddConsumableAsync("Glue"); // no threshold — never low

        var resp = await _sut.Handle(new GetConsumablesQuery { Page = 1, PageSize = 50, LowStockOnly = true }, Ct);

        Assert.Equal(2, resp.Items.Count);
        Assert.Contains(resp.Items, s => s.Name == "Wood screws" && s.TotalOnHand == 4);
        Assert.Contains(resp.Items, s => s.Name == "Sandpaper" && s.TotalOnHand == 5);
        Assert.All(resp.Items, s => Assert.True(s.IsLow));
    }

    [Fact]
    public async Task Handle_FiltersByArea_MatchesStockResolvingToArea()
    {
        var office = await AddAreaAsync("Office");
        var screws = await AddBareConsumableAsync("Wood screws");
        await AddStockAsync(screws.Id, 6, areaId: _garage.Id);
        var clips = await AddBareConsumableAsync("Paper clips");
        await AddStockAsync(clips.Id, 3, areaId: office.Id);

        var resp = await _sut.Handle(new GetConsumablesQuery { Page = 1, PageSize = 50, AreaId = _garage.Id }, Ct);

        Assert.Single(resp.Items);
        Assert.Equal("Wood screws", resp.Items[0].Name);
    }

    [Fact]
    public async Task Handle_FiltersByArea_MatchesStockInContainerResolvingToArea()
    {
        var chest = await AddContainerAsync("Tool chest", areaId: _garage.Id);
        var screws = await AddBareConsumableAsync("Wood screws");
        await AddStockAsync(screws.Id, 4, containerItemId: chest.Id); // stock inside a garage container

        var resp = await _sut.Handle(new GetConsumablesQuery { Page = 1, PageSize = 50, AreaId = _garage.Id }, Ct);

        Assert.Single(resp.Items);
        Assert.Equal("Wood screws", resp.Items[0].Name);
    }

    [Fact]
    public async Task Handle_FiltersByArea_CountsMultiLocationConsumableOnce()
    {
        var chest = await AddContainerAsync("Tool chest", areaId: _garage.Id);
        var screws = await AddBareConsumableAsync("Wood screws");
        await AddStockAsync(screws.Id, 6, areaId: _garage.Id);        // resolves to garage
        await AddStockAsync(screws.Id, 4, containerItemId: chest.Id); // also resolves to garage

        var resp = await _sut.Handle(new GetConsumablesQuery { Page = 1, PageSize = 50, AreaId = _garage.Id }, Ct);

        Assert.Single(resp.Items); // counted once despite two matching stock locations
        Assert.Equal(1, resp.TotalCount);
    }

    [Fact]
    public async Task Handle_FiltersByArea_ExpandsToDescendantAreas()
    {
        var bench = await AddAreaAsync("Workbench", parentAreaId: _garage.Id);
        var screws = await AddBareConsumableAsync("Wood screws");
        await AddStockAsync(screws.Id, 6, areaId: bench.Id); // stocked in a sub-area of the garage

        var resp = await _sut.Handle(new GetConsumablesQuery { Page = 1, PageSize = 50, AreaId = _garage.Id }, Ct);

        Assert.Single(resp.Items);
        Assert.Equal("Wood screws", resp.Items[0].Name);
    }

    [Fact]
    public async Task Handle_FiltersUnplaced_MatchesStockWithNoAreaRoot()
    {
        var floatingBin = await AddContainerAsync("Floating bin"); // no area — a broken chain
        var washers = await AddBareConsumableAsync("Washers");
        await AddStockAsync(washers.Id, 3, containerItemId: floatingBin.Id);
        var placed = await AddBareConsumableAsync("Wood screws");
        await AddStockAsync(placed.Id, 6, areaId: _garage.Id);

        var resp = await _sut.Handle(new GetConsumablesQuery { Page = 1, PageSize = 50, Unplaced = true }, Ct);

        Assert.Single(resp.Items);
        Assert.Equal("Washers", resp.Items[0].Name);
    }

    [Fact]
    public async Task Handle_AreaAndUnplaced_ExcludeConsumablesWithNoStock()
    {
        var screws = await AddBareConsumableAsync("Wood screws");
        await AddStockAsync(screws.Id, 6, areaId: _garage.Id);
        await AddBareConsumableAsync("Glue"); // zero stock — matches neither bucket

        var area = await _sut.Handle(new GetConsumablesQuery { Page = 1, PageSize = 50, AreaId = _garage.Id }, Ct);
        Assert.DoesNotContain(area.Items, s => s.Name == "Glue");

        var unplaced = await _sut.Handle(new GetConsumablesQuery { Page = 1, PageSize = 50, Unplaced = true }, Ct);
        Assert.DoesNotContain(unplaced.Items, s => s.Name == "Glue");
    }

    [Fact]
    public async Task Handle_FiltersByArea_TotalCountReflectsFilteredSet_NotPageLocal()
    {
        for (var i = 0; i < 5; i++)
        {
            var c = await AddBareConsumableAsync($"Garage stock {i:D2}");
            await AddStockAsync(c.Id, 1, areaId: _garage.Id);
        }
        var office = await AddAreaAsync("Office");
        var pen = await AddBareConsumableAsync("Office pen");
        await AddStockAsync(pen.Id, 1, areaId: office.Id);

        var resp = await _sut.Handle(new GetConsumablesQuery { Page = 1, PageSize = 2, AreaId = _garage.Id }, Ct);

        Assert.Equal(2, resp.Items.Count); // one page
        Assert.Equal(5, resp.TotalCount);  // whole filtered set, not the page or the office pen
    }
}
