using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Areas.GetAreas;
using Homestead.WebApi.Modules.Inventory.Services;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Features.Areas;

public class GetAreasHandlerTests : IAsyncLifetime
{
    private TestAppDbContext _db = null!;
    private GetAreasHandler _sut = null!;

    public ValueTask InitializeAsync()
    {
        _db = TestAppDbContext.Create();
        _sut = new GetAreasHandler(_db, new InventoryPlacementService(_db));
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Handle_LiveItemCount_ExcludesTerminalAndUnplaced()
    {
        var garage = new Area { Id = Guid.NewGuid(), Name = "Garage" };
        _db.Areas.Add(garage);

        // Two live in the garage, one terminal in the garage, one live elsewhere (unplaced).
        _db.Items.AddRange(
            Item(garage.Id, ItemStatus.Active),
            Item(garage.Id, ItemStatus.Lent),
            Item(garage.Id, ItemStatus.Sold),
            Item(null, ItemStatus.Active));
        await _db.SaveChangesAsync(Ct);

        var resp = await _sut.Handle(new GetAreasQuery { Page = 1, PageSize = 50 }, Ct);

        var area = Assert.Single(resp.Items);
        Assert.Equal("Garage", area.Name);
        Assert.Equal(2, area.LiveItemCount);
    }

    [Fact]
    public async Task Handle_LiveItemCount_ResolvesNestedItemsViaContainerChain()
    {
        var garage = new Area { Id = Guid.NewGuid(), Name = "Garage" };
        _db.Areas.Add(garage);

        var bin = Item(garage.Id, ItemStatus.Active);
        bin.IsContainer = true;
        var nested = Item(null, ItemStatus.Active);
        nested.ParentItemId = bin.Id;

        // Item inside an unplaced container resolves to no area.
        var floatingBin = Item(null, ItemStatus.Active);
        floatingBin.IsContainer = true;
        var adrift = Item(null, ItemStatus.Active);
        adrift.ParentItemId = floatingBin.Id;

        _db.Items.AddRange(bin, nested, floatingBin, adrift);
        await _db.SaveChangesAsync(Ct);

        var resp = await _sut.Handle(new GetAreasQuery { Page = 1, PageSize = 50 }, Ct);

        var area = Assert.Single(resp.Items);
        Assert.Equal(2, area.LiveItemCount); // the bin + the nested item
    }

    [Fact]
    public async Task Handle_LiveItemCount_CountsDistinctSuppliesWithStockResolvingHere()
    {
        var garage = new Area { Id = Guid.NewGuid(), Name = "Garage" };
        _db.Areas.Add(garage);

        var bin = Item(garage.Id, ItemStatus.Active);
        bin.IsContainer = true;
        _db.Items.Add(bin);

        // Two entries for the same supply resolving to the garage (direct + via the bin) ⇒ one type.
        var batteries = Supply(ItemStatus.Active);
        _db.Items.Add(batteries);
        _db.StockEntries.AddRange(
            Stock(batteries.Id, areaId: garage.Id),
            Stock(batteries.Id, containerItemId: bin.Id));

        // Archived supplies keep stock entries but are hidden from the list — excluded.
        var archived = Supply(ItemStatus.Archived);
        _db.Items.Add(archived);
        _db.StockEntries.Add(Stock(archived.Id, areaId: garage.Id));

        await _db.SaveChangesAsync(Ct);

        var resp = await _sut.Handle(new GetAreasQuery { Page = 1, PageSize = 50 }, Ct);

        var area = Assert.Single(resp.Items);
        Assert.Equal(2, area.LiveItemCount); // the bin + batteries once
    }

    [Fact]
    public async Task Handle_OrdersByName()
    {
        _db.Areas.AddRange(
            new Area { Id = Guid.NewGuid(), Name = "Office" },
            new Area { Id = Guid.NewGuid(), Name = "Attic" });
        await _db.SaveChangesAsync(Ct);

        var resp = await _sut.Handle(new GetAreasQuery { Page = 1, PageSize = 50 }, Ct);

        Assert.Equal(["Attic", "Office"], resp.Items.Select(a => a.Name));
    }

    private static Item Item(Guid? areaId, ItemStatus status) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Thing",
        Fungibility = Fungibility.NonFungible,
        Status = status,
        AreaId = areaId,
    };

    private static Item Supply(ItemStatus status) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Supply",
        Fungibility = Fungibility.Fungible,
        Status = status,
        Unit = "units",
    };

    private static StockEntry Stock(Guid itemId, Guid? areaId = null, Guid? containerItemId = null) => new()
    {
        Id = Guid.NewGuid(),
        ItemId = itemId,
        AreaId = areaId,
        ContainerItemId = containerItemId,
        Quantity = 1m,
    };
}
