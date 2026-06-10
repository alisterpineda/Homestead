using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Items.GetItems;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Features.Items;

public class GetItemsHandlerTests : IAsyncLifetime
{
    private TestAppDbContext _db = null!;
    private GetItemsHandler _sut = null!;
    private Area _garage = null!;
    private Area _office = null!;

    public async ValueTask InitializeAsync()
    {
        _db = TestAppDbContext.Create();
        _garage = new Area { Id = Guid.NewGuid(), Name = "Garage" };
        _office = new Area { Id = Guid.NewGuid(), Name = "Office" };
        _db.Areas.AddRange(_garage, _office);
        await _db.SaveChangesAsync();
        _sut = new GetItemsHandler(_db);
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Item> AddItemAsync(string name, ItemStatus status = ItemStatus.Active, Guid? areaId = null)
    {
        var item = new Item
        {
            Id = Guid.NewGuid(),
            Name = name,
            Fungibility = Fungibility.NonFungible,
            Status = status,
            AreaId = areaId,
        };
        _db.Items.Add(item);
        await _db.SaveChangesAsync(Ct);
        return item;
    }

    [Fact]
    public async Task Handle_HidesTerminalItemsByDefault()
    {
        await AddItemAsync("Drill");
        await AddItemAsync("Old TV", ItemStatus.Discarded);
        await AddItemAsync("Sold bike", ItemStatus.Sold);
        await AddItemAsync("Archived box", ItemStatus.Archived);

        var resp = await _sut.Handle(new GetItemsQuery { Page = 1, PageSize = 50 }, Ct);

        Assert.Single(resp.Items);
        Assert.Equal("Drill", resp.Items[0].Name);
    }

    [Fact]
    public async Task Handle_IncludeInactive_ShowsTerminalItems()
    {
        await AddItemAsync("Drill");
        await AddItemAsync("Old TV", ItemStatus.Discarded);

        var resp = await _sut.Handle(new GetItemsQuery { Page = 1, PageSize = 50, IncludeInactive = true }, Ct);

        Assert.Equal(2, resp.Items.Count);
    }

    [Fact]
    public async Task Handle_FiltersByArea()
    {
        await AddItemAsync("Drill", areaId: _garage.Id);
        await AddItemAsync("Stapler", areaId: _office.Id);

        var resp = await _sut.Handle(new GetItemsQuery { Page = 1, PageSize = 50, AreaId = _garage.Id }, Ct);

        Assert.Single(resp.Items);
        Assert.Equal("Drill", resp.Items[0].Name);
        Assert.Equal("Garage", resp.Items[0].AreaName);
    }

    [Fact]
    public async Task Handle_FiltersUnplaced()
    {
        await AddItemAsync("Drill", areaId: _garage.Id);
        await AddItemAsync("Floating thing");

        var resp = await _sut.Handle(new GetItemsQuery { Page = 1, PageSize = 50, Unplaced = true }, Ct);

        Assert.Single(resp.Items);
        Assert.Equal("Floating thing", resp.Items[0].Name);
        Assert.Null(resp.Items[0].AreaId);
    }

    [Fact]
    public async Task Handle_FiltersBySearch()
    {
        await AddItemAsync("Cordless drill");
        await AddItemAsync("Stapler");

        var resp = await _sut.Handle(new GetItemsQuery { Page = 1, PageSize = 50, Search = "drill" }, Ct);

        Assert.Single(resp.Items);
        Assert.Equal("Cordless drill", resp.Items[0].Name);
    }
}
