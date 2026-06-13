using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Items;
using Homestead.WebApi.Modules.Inventory.Features.Places.GetPlaceContents;
using Homestead.WebApi.Modules.Inventory.Services;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Features.Places;

public class GetPlaceContentsHandlerTests : IAsyncLifetime
{
    private TestAppDbContext _db = null!;
    private GetPlaceContentsHandler _sut = null!;
    private Area _garage = null!;

    public async ValueTask InitializeAsync()
    {
        _db = TestAppDbContext.Create();
        _garage = new Area { Id = Guid.NewGuid(), Name = "Garage" };
        _db.Areas.Add(_garage);
        await _db.SaveChangesAsync();
        _sut = new GetPlaceContentsHandler(
            _db, new InventoryPlacementService(_db), new ItemLocationResolver(_db));
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

    private async Task<Item> AddSupplyAsync(
        string name, decimal? threshold = null, ItemStatus status = ItemStatus.Active)
    {
        var supply = new Item
        {
            Id = Guid.NewGuid(),
            Name = name,
            Fungibility = Fungibility.Fungible,
            Status = status,
            Unit = "units",
            RestockThreshold = threshold,
        };
        _db.Items.Add(supply);
        await _db.SaveChangesAsync(Ct);
        return supply;
    }

    private async Task AddStockAsync(
        Guid supplyId, decimal quantity, Guid? areaId = null, Guid? containerItemId = null)
    {
        _db.StockEntries.Add(new StockEntry
        {
            Id = Guid.NewGuid(),
            ItemId = supplyId,
            AreaId = areaId,
            ContainerItemId = containerItemId,
            Quantity = quantity,
        });
        await _db.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task Handle_NoScope_ThrowsBusinessRule()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(
            () => _sut.Handle(new GetPlaceContentsQuery(), Ct));
    }

    [Fact]
    public async Task Handle_TwoScopes_ThrowsBusinessRule()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(
            () => _sut.Handle(new GetPlaceContentsQuery
            {
                AreaId = _garage.Id,
                Unplaced = true,
            }, Ct));
    }

    [Fact]
    public async Task Handle_UnknownArea_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => _sut.Handle(new GetPlaceContentsQuery { AreaId = Guid.NewGuid() }, Ct));
    }

    [Fact]
    public async Task Handle_NonContainerItem_ThrowsNotFound()
    {
        var item = await AddItemAsync("Drill", areaId: _garage.Id);

        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => _sut.Handle(new GetPlaceContentsQuery { ContainerItemId = item.Id }, Ct));
    }

    [Fact]
    public async Task Handle_AreaScope_SplitsContainersFromItemsWithSingleSegmentPath()
    {
        var chest = await AddItemAsync("Chest", areaId: _garage.Id, isContainer: true);
        var drill = await AddItemAsync("Drill", areaId: _garage.Id);
        await AddItemAsync("Elsewhere", parentItemId: chest.Id); // not direct in the area

        var resp = await _sut.Handle(new GetPlaceContentsQuery { AreaId = _garage.Id }, Ct);

        var segment = Assert.Single(resp.Path);
        Assert.Equal(_garage.Id, segment.Id);
        Assert.Equal("Garage", segment.Name);
        Assert.Equal(LocationSegmentKind.Area, segment.Kind);
        Assert.Null(resp.ContainerItem);
        Assert.Equal(chest.Id, Assert.Single(resp.Containers).Id);
        Assert.Equal(drill.Id, Assert.Single(resp.Items).Id);
    }

    [Fact]
    public async Task Handle_ContainerScope_BreadcrumbIncludesSelfAndEnrichesContainer()
    {
        var chest = await AddItemAsync("Chest", areaId: _garage.Id, isContainer: true);
        var bin = await AddItemAsync("Bin", parentItemId: chest.Id, isContainer: true);

        var resp = await _sut.Handle(new GetPlaceContentsQuery { ContainerItemId = bin.Id }, Ct);

        Assert.Equal(3, resp.Path.Count); // Garage › Chest › Bin
        Assert.Equal(LocationSegmentKind.Area, resp.Path[0].Kind);
        Assert.Equal("Garage", resp.Path[0].Name);
        Assert.Equal("Chest", resp.Path[1].Name);
        Assert.Equal("Bin", resp.Path[2].Name);
        Assert.Equal(LocationSegmentKind.Container, resp.Path[2].Kind);

        Assert.NotNull(resp.ContainerItem);
        Assert.Equal(bin.Id, resp.ContainerItem.Id);
        // Enriched like a list row: resolved area + path to (not including) itself.
        Assert.Equal(_garage.Id, resp.ContainerItem.ResolvedAreaId);
        Assert.Equal(2, resp.ContainerItem.Path.Count);
    }

    [Fact]
    public async Task Handle_ContainerScope_SuppliesCarryLocalQuantityAndFullStockLocations()
    {
        var chest = await AddItemAsync("Chest", areaId: _garage.Id, isContainer: true);
        var screws = await AddSupplyAsync("Screws");
        await AddStockAsync(screws.Id, 10m, containerItemId: chest.Id);
        await AddStockAsync(screws.Id, 5m, areaId: _garage.Id); // elsewhere

        var resp = await _sut.Handle(new GetPlaceContentsQuery { ContainerItemId = chest.Id }, Ct);

        var supply = Assert.Single(resp.Supplies);
        Assert.Equal(screws.Id, supply.Supply.Id);
        Assert.Equal(10m, supply.LocalQuantity);
        Assert.Equal(15m, supply.Supply.TotalOnHand);
        // GetSupplies parity: the full per-location breakdown rides along for the quick panel.
        Assert.Equal(2, supply.Supply.StockLocations.Count);
        Assert.Contains(supply.Supply.StockLocations, e => e.ContainerItemId == chest.Id && e.Quantity == 10m);
        Assert.Contains(supply.Supply.StockLocations, e => e.AreaId == _garage.Id && e.Quantity == 5m);
        Assert.All(supply.Supply.StockLocations, e => Assert.NotNull(e.LocationLabel));
    }

    [Fact]
    public async Task Handle_UnplacedScope_ItemsOnlyWithEmptyPath()
    {
        var floatingBin = await AddItemAsync("Floating bin", isContainer: true);
        var adrift = await AddItemAsync("Adrift wrench");
        await AddItemAsync("Placed drill", areaId: _garage.Id);
        await AddItemAsync("Nested", parentItemId: floatingBin.Id); // resolved-unplaced, not direct

        var resp = await _sut.Handle(new GetPlaceContentsQuery { Unplaced = true }, Ct);

        Assert.Empty(resp.Path);
        Assert.Null(resp.ContainerItem);
        Assert.Equal(floatingBin.Id, Assert.Single(resp.Containers).Id);
        Assert.Equal(adrift.Id, Assert.Single(resp.Items).Id);
        Assert.Empty(resp.Supplies);
        // Direct = bin + wrench; the resolved-unplaced total adds the nested item.
        Assert.Equal(2, resp.DirectItemCount);
        Assert.Equal(1, resp.NestedItemCount);
    }

    [Fact]
    public async Task Handle_AreaScope_NestedCountMath()
    {
        var chest = await AddItemAsync("Chest", areaId: _garage.Id, isContainer: true);
        await AddItemAsync("Direct drill", areaId: _garage.Id);
        await AddItemAsync("Nested wrench", parentItemId: chest.Id);
        var screws = await AddSupplyAsync("Screws");
        await AddStockAsync(screws.Id, 10m, containerItemId: chest.Id);
        var tape = await AddSupplyAsync("Tape");
        await AddStockAsync(tape.Id, 2m, areaId: _garage.Id);

        var resp = await _sut.Handle(new GetPlaceContentsQuery { AreaId = _garage.Id }, Ct);

        // Direct: chest + drill + tape. Nested: wrench + screws (inside the chest).
        Assert.Equal(3, resp.DirectItemCount);
        Assert.Equal(2, resp.NestedItemCount);
        var chestCard = Assert.Single(resp.Containers);
        Assert.Equal(2, chestCard.ItemCount);
    }

    [Fact]
    public async Task Handle_PreviewNames_CappedAtThreeAndNameSorted()
    {
        var chest = await AddItemAsync("Chest", areaId: _garage.Id, isContainer: true);
        await AddItemAsync("Zebra print", parentItemId: chest.Id);
        await AddItemAsync("anvil", parentItemId: chest.Id);
        await AddItemAsync("Mallet", parentItemId: chest.Id);
        var bolts = await AddSupplyAsync("Bolts");
        await AddStockAsync(bolts.Id, 10m, containerItemId: chest.Id);

        var resp = await _sut.Handle(new GetPlaceContentsQuery { AreaId = _garage.Id }, Ct);

        var card = Assert.Single(resp.Containers);
        // Case-insensitive name sort across items + supplies, capped at 3.
        Assert.Equal(new[] { "anvil", "Bolts", "Mallet" }, card.PreviewNames);
    }

    [Fact]
    public async Task Handle_TerminalChildrenAndArchivedSupplies_Excluded()
    {
        var chest = await AddItemAsync("Chest", areaId: _garage.Id, isContainer: true);
        await AddItemAsync("Sold drill", status: ItemStatus.Sold, areaId: _garage.Id);
        await AddItemAsync("Archived nested", status: ItemStatus.Archived, parentItemId: chest.Id);
        var archived = await AddSupplyAsync("Old glue", status: ItemStatus.Archived);
        await AddStockAsync(archived.Id, 3m, areaId: _garage.Id);
        await AddStockAsync(archived.Id, 2m, containerItemId: chest.Id);

        var resp = await _sut.Handle(new GetPlaceContentsQuery { AreaId = _garage.Id }, Ct);

        Assert.Empty(resp.Items);
        Assert.Empty(resp.Supplies);
        var card = Assert.Single(resp.Containers);
        Assert.Equal(0, card.ItemCount);
        Assert.Empty(card.PreviewNames);
        Assert.Equal(1, resp.DirectItemCount); // the chest itself
        Assert.Equal(0, resp.NestedItemCount);
    }

    [Fact]
    public async Task Handle_TerminalContainerScope_AllowedAndRendersLiveContents()
    {
        var archivedChest = await AddItemAsync(
            "Archived chest", status: ItemStatus.Archived, areaId: _garage.Id, isContainer: true);
        var wrench = await AddItemAsync("Wrench", parentItemId: archivedChest.Id);

        var resp = await _sut.Handle(
            new GetPlaceContentsQuery { ContainerItemId = archivedChest.Id }, Ct);

        Assert.Equal(archivedChest.Id, resp.ContainerItem!.Id);
        Assert.Equal(wrench.Id, Assert.Single(resp.Items).Id);
    }

    [Fact]
    public async Task Handle_EmptyContainer_ReturnsEmptyLists()
    {
        var chest = await AddItemAsync("Chest", areaId: _garage.Id, isContainer: true);

        var resp = await _sut.Handle(new GetPlaceContentsQuery { ContainerItemId = chest.Id }, Ct);

        Assert.Empty(resp.Containers);
        Assert.Empty(resp.Items);
        Assert.Empty(resp.Supplies);
        Assert.Equal(0, resp.DirectItemCount);
        Assert.Equal(0, resp.NestedItemCount);
    }
}
