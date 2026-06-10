using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Items.GetItems;
using Homestead.WebApi.Modules.Inventory.Services;

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
        _sut = new GetItemsHandler(_db, new ItemLocationResolver(_db));
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Item> AddItemAsync(
        string name, ItemStatus status = ItemStatus.Active, Guid? areaId = null,
        Guid? parentItemId = null, bool isContainer = false)
    {
        var item = new Item
        {
            Id = Guid.NewGuid(),
            Name = name,
            Fungibility = Fungibility.NonFungible,
            Status = status,
            AreaId = areaId,
            ParentItemId = parentItemId,
            IsContainer = isContainer,
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

    [Fact]
    public async Task Handle_FiltersByParentItemId_ReturnsDirectChildrenOnly()
    {
        var chest = await AddItemAsync("Tool chest", areaId: _garage.Id, isContainer: true);
        var bin = await AddItemAsync("Bin", parentItemId: chest.Id, isContainer: true);
        await AddItemAsync("Wrench", parentItemId: chest.Id); // direct child
        await AddItemAsync("Screw driver", parentItemId: bin.Id); // grandchild

        var resp = await _sut.Handle(
            new GetItemsQuery { Page = 1, PageSize = 50, ParentItemId = chest.Id }, Ct);

        Assert.Equal(2, resp.Items.Count); // bin + wrench, not the grandchild
        Assert.Contains(resp.Items, i => i.Name == "Bin");
        Assert.Contains(resp.Items, i => i.Name == "Wrench");
        Assert.DoesNotContain(resp.Items, i => i.Name == "Screw driver");
    }

    [Fact]
    public async Task Handle_FiltersByIsContainer()
    {
        await AddItemAsync("Tool chest", areaId: _garage.Id, isContainer: true);
        await AddItemAsync("Wrench", areaId: _garage.Id);

        var resp = await _sut.Handle(
            new GetItemsQuery { Page = 1, PageSize = 50, IsContainer = true }, Ct);

        Assert.Single(resp.Items);
        Assert.Equal("Tool chest", resp.Items[0].Name);
    }
}
