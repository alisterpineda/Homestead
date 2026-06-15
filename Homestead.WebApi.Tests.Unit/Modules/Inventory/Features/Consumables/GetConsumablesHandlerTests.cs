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
}
