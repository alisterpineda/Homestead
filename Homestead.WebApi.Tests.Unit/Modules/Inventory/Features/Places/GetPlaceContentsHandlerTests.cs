using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Durables;
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

    private async Task<Area> AddAreaAsync(string name, Guid? parentAreaId = null)
    {
        var area = new Area { Id = Guid.NewGuid(), Name = name, ParentAreaId = parentAreaId };
        _db.Areas.Add(area);
        await _db.SaveChangesAsync(Ct);
        return area;
    }

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

    private async Task<Item> AddConsumableAsync(
        string name, decimal? threshold = null, ItemStatus status = ItemStatus.Active)
    {
        var consumable = new Item
        {
            Id = Guid.NewGuid(),
            Name = name,
            Fungibility = Fungibility.Fungible,
            Status = status,
            Unit = "units",
            RestockThreshold = threshold,
        };
        _db.Items.Add(consumable);
        await _db.SaveChangesAsync(Ct);
        return consumable;
    }

    private async Task AddStockAsync(
        Guid consumableId, decimal quantity, Guid? areaId = null, Guid? containerItemId = null)
    {
        _db.StockEntries.Add(new StockEntry
        {
            Id = Guid.NewGuid(),
            ItemId = consumableId,
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
    public async Task Handle_FungibleConsumableScope_ThrowsNotFound()
    {
        // Any non-fungible item is a valid scope (a container's contents or a regular item's
        // components), but a fungible consumable is not a place/holder — scoping to it is a 404.
        var consumable = await AddConsumableAsync("Screws");

        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => _sut.Handle(new GetPlaceContentsQuery { ContainerItemId = consumable.Id }, Ct));
    }

    [Fact]
    public async Task Handle_RegularItemScope_ReturnsEmptyComponents()
    {
        // A regular (non-container) item with no components is still a valid scope — it just returns an
        // empty component list, which the detail page renders as "No components yet".
        var drill = await AddItemAsync("Drill", areaId: _garage.Id);

        var resp = await _sut.Handle(new GetPlaceContentsQuery { ContainerItemId = drill.Id }, Ct);

        Assert.NotNull(resp.ContainerItem);
        Assert.Equal(drill.Id, resp.ContainerItem.Id);
        Assert.False(resp.ContainerItem.IsContainer);
        Assert.False(resp.ContainerItem.HasComponents);
        Assert.Empty(resp.Items);
        Assert.Empty(resp.Containers);
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
    public async Task Handle_AreaScope_AssemblyIsAnItemNotADrillCard_ComponentsAbsent()
    {
        // An Assembly is not a place: it renders as a plain item row in its area, NOT a drill card,
        // and its components never appear in the area's drill-in (their parent is the assembly).
        var pc = await AddItemAsync("Gaming PC", areaId: _garage.Id, isContainer: false);
        await AddItemAsync("CPU", parentItemId: pc.Id);

        var resp = await _sut.Handle(new GetPlaceContentsQuery { AreaId = _garage.Id }, Ct);

        Assert.Empty(resp.Containers);
        Assert.Equal(pc.Id, Assert.Single(resp.Items).Id);
        Assert.DoesNotContain(resp.Items, i => i.Name == "CPU");
    }

    [Fact]
    public async Task Handle_AssemblyScope_ReturnsComponentsAsItemsNoConsumables()
    {
        // The same endpoint, called with the assembly id, returns its components as items — reused by
        // the detail page's Components section.
        var pc = await AddItemAsync("Gaming PC", areaId: _garage.Id, isContainer: false);
        var cpu = await AddItemAsync("CPU", parentItemId: pc.Id);

        var resp = await _sut.Handle(new GetPlaceContentsQuery { ContainerItemId = pc.Id }, Ct);

        Assert.NotNull(resp.ContainerItem);
        Assert.Equal(pc.Id, resp.ContainerItem.Id);
        Assert.Equal(false, resp.ContainerItem.IsContainer);
        Assert.True(resp.ContainerItem.HasComponents);
        Assert.Equal(cpu.Id, Assert.Single(resp.Items).Id);
        Assert.Empty(resp.Containers);
        Assert.Empty(resp.Consumables);
    }

    [Fact]
    public async Task Handle_AssemblyScope_ContainerComponentRendersAsItemNotDrillCard()
    {
        // A component can itself be a Container; under an assembly scope it must still appear as a
        // component (items), never split into a drillable place-card — an assembly is not a place.
        var pc = await AddItemAsync("Gaming PC", areaId: _garage.Id, isContainer: false);
        var driveCage = await AddItemAsync(
            "Drive cage", parentItemId: pc.Id, isContainer: true);

        var resp = await _sut.Handle(new GetPlaceContentsQuery { ContainerItemId = pc.Id }, Ct);

        Assert.Empty(resp.Containers);
        Assert.Equal(driveCage.Id, Assert.Single(resp.Items).Id);
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
    public async Task Handle_ContainerScope_ConsumablesCarryLocalQuantityAndFullStockLocations()
    {
        var chest = await AddItemAsync("Chest", areaId: _garage.Id, isContainer: true);
        var screws = await AddConsumableAsync("Screws");
        await AddStockAsync(screws.Id, 10m, containerItemId: chest.Id);
        await AddStockAsync(screws.Id, 5m, areaId: _garage.Id); // elsewhere

        var resp = await _sut.Handle(new GetPlaceContentsQuery { ContainerItemId = chest.Id }, Ct);

        var consumable = Assert.Single(resp.Consumables);
        Assert.Equal(screws.Id, consumable.Consumable.Id);
        Assert.Equal(10m, consumable.LocalQuantity);
        Assert.Equal(15m, consumable.Consumable.TotalOnHand);
        // GetConsumables parity: the full per-location breakdown rides along for the quick panel.
        Assert.Equal(2, consumable.Consumable.StockLocations.Count);
        Assert.Contains(consumable.Consumable.StockLocations, e => e.ContainerItemId == chest.Id && e.Quantity == 10m);
        Assert.Contains(consumable.Consumable.StockLocations, e => e.AreaId == _garage.Id && e.Quantity == 5m);
        Assert.All(consumable.Consumable.StockLocations, e => Assert.NotNull(e.LocationLabel));
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
        Assert.Empty(resp.Consumables);
    }

    [Fact]
    public async Task Handle_AreaScope_SplitsDirectContentsAndCardCarriesRolledSubtreeCount()
    {
        var chest = await AddItemAsync("Chest", areaId: _garage.Id, isContainer: true);
        var drill = await AddItemAsync("Direct drill", areaId: _garage.Id);
        await AddItemAsync("Nested wrench", parentItemId: chest.Id);
        var screws = await AddConsumableAsync("Screws");
        await AddStockAsync(screws.Id, 10m, containerItemId: chest.Id);
        var tape = await AddConsumableAsync("Tape");
        await AddStockAsync(tape.Id, 2m, areaId: _garage.Id);

        var resp = await _sut.Handle(new GetPlaceContentsQuery { AreaId = _garage.Id }, Ct);

        // The area's direct contents split into their cards/rows: chest (container), drill (item), tape (consumable).
        var chestCard = Assert.Single(resp.Containers);
        Assert.Equal(chest.Id, chestCard.Id);
        Assert.Equal(drill.Id, Assert.Single(resp.Items).Id);
        Assert.Equal(tape.Id, Assert.Single(resp.Consumables).Consumable.Id);
        // The container card carries its rolled subtree count: wrench + screws inside the chest.
        Assert.Equal(2, chestCard.ItemCount);
    }

    [Fact]
    public async Task Handle_PreviewNames_CappedAtThreeAndNameSorted()
    {
        var chest = await AddItemAsync("Chest", areaId: _garage.Id, isContainer: true);
        await AddItemAsync("Zebra print", parentItemId: chest.Id);
        await AddItemAsync("anvil", parentItemId: chest.Id);
        await AddItemAsync("Mallet", parentItemId: chest.Id);
        var bolts = await AddConsumableAsync("Bolts");
        await AddStockAsync(bolts.Id, 10m, containerItemId: chest.Id);

        var resp = await _sut.Handle(new GetPlaceContentsQuery { AreaId = _garage.Id }, Ct);

        var card = Assert.Single(resp.Containers);
        // Case-insensitive name sort across items + consumables, capped at 3.
        Assert.Equal(new[] { "anvil", "Bolts", "Mallet" }, card.PreviewNames);
    }

    [Fact]
    public async Task Handle_TerminalChildrenAndArchivedConsumables_Excluded()
    {
        var chest = await AddItemAsync("Chest", areaId: _garage.Id, isContainer: true);
        await AddItemAsync("Sold drill", status: ItemStatus.Sold, areaId: _garage.Id);
        await AddItemAsync("Archived nested", status: ItemStatus.Archived, parentItemId: chest.Id);
        var archived = await AddConsumableAsync("Old glue", status: ItemStatus.Archived);
        await AddStockAsync(archived.Id, 3m, areaId: _garage.Id);
        await AddStockAsync(archived.Id, 2m, containerItemId: chest.Id);

        var resp = await _sut.Handle(new GetPlaceContentsQuery { AreaId = _garage.Id }, Ct);

        Assert.Empty(resp.Items);
        Assert.Empty(resp.Consumables);
        var card = Assert.Single(resp.Containers);
        Assert.Equal(0, card.ItemCount);
        Assert.Empty(card.PreviewNames);
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
        Assert.Empty(resp.Consumables);
    }

    [Fact]
    public async Task Handle_AreaScope_RendersChildAreaCardsWithFullBreadcrumb()
    {
        var house = await AddAreaAsync("House");
        var basement = await AddAreaAsync("Basement", parentAreaId: house.Id);
        var bedroom = await AddAreaAsync("Bedroom", parentAreaId: basement.Id);
        await AddItemAsync("Lamp", areaId: bedroom.Id);

        var resp = await _sut.Handle(new GetPlaceContentsQuery { AreaId = basement.Id }, Ct);

        // Full ancestor breadcrumb, and Bedroom as a child-area card carrying its rolled count.
        Assert.Equal(["House", "Basement"], resp.Path.Select(s => s.Name));
        var childCard = Assert.Single(resp.ChildAreas);
        Assert.Equal(bedroom.Id, childCard.Id);
        Assert.Equal(1, childCard.ItemCount);
        // An area whose only contents are sub-areas has empty direct lists — the lamp lives in Bedroom.
        Assert.Empty(resp.Containers);
        Assert.Empty(resp.Items);
        Assert.Empty(resp.Consumables);
    }

    [Fact]
    public async Task Handle_AreaScope_PopulatesScopedLowStockAndLentCounts()
    {
        var chest = await AddItemAsync("Chest", areaId: _garage.Id, isContainer: true);
        await AddItemAsync("Lent ladder", status: ItemStatus.Lent, areaId: _garage.Id);
        await AddItemAsync("Lent wrench", status: ItemStatus.Lent, parentItemId: chest.Id);
        var lowGlue = await AddConsumableAsync("Glue", threshold: 5m);
        await AddStockAsync(lowGlue.Id, 2m, areaId: _garage.Id);

        var resp = await _sut.Handle(new GetPlaceContentsQuery { AreaId = _garage.Id }, Ct);

        Assert.Equal(1, resp.ScopedLowStockCount);
        Assert.Equal(2, resp.ScopedLentCount); // ladder (direct) + wrench (nested in the chest)
    }

    [Fact]
    public async Task Handle_ContainerScope_PopulatesScopedLowStockAndLentCounts()
    {
        var chest = await AddItemAsync("Chest", areaId: _garage.Id, isContainer: true);
        await AddItemAsync("Lent wrench", status: ItemStatus.Lent, parentItemId: chest.Id);
        var lowGlue = await AddConsumableAsync("Glue", threshold: 5m);
        await AddStockAsync(lowGlue.Id, 2m, containerItemId: chest.Id);

        var resp = await _sut.Handle(new GetPlaceContentsQuery { ContainerItemId = chest.Id }, Ct);

        Assert.Equal(1, resp.ScopedLowStockCount);
        Assert.Equal(1, resp.ScopedLentCount);
    }

    [Fact]
    public async Task Handle_UnplacedScope_HasZeroScopedWarningCounts()
    {
        await AddItemAsync("Adrift wrench"); // resolved-unplaced

        var resp = await _sut.Handle(new GetPlaceContentsQuery { Unplaced = true }, Ct);

        Assert.Equal(0, resp.ScopedLowStockCount);
        Assert.Equal(0, resp.ScopedLentCount);
    }
}
