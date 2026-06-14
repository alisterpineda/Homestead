using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Services;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Services;

public class InventoryPlacementServiceTests : IAsyncLifetime
{
    private TestAppDbContext _db = null!;
    private InventoryPlacementService _sut = null!;
    private Area _garage = null!;

    public async ValueTask InitializeAsync()
    {
        _db = TestAppDbContext.Create();
        _garage = new Area { Id = Guid.NewGuid(), Name = "Garage" };
        _db.Areas.Add(_garage);
        await _db.SaveChangesAsync();
        _sut = new InventoryPlacementService(_db);
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
    public async Task Compute_NestedItem_CountsTowardEveryAncestorContainer()
    {
        var chest = await AddItemAsync("Chest", areaId: _garage.Id, isContainer: true);
        var bin = await AddItemAsync("Bin", parentItemId: chest.Id, isContainer: true);
        var wrench = await AddItemAsync("Wrench", parentItemId: bin.Id);

        var placement = await _sut.ComputeAsync(Ct);

        // The wrench counts toward the bin AND the chest; the bin counts toward the chest.
        Assert.Equal(1, placement.ContainerStats[bin.Id].ResolvedContentsCount);
        Assert.Equal(2, placement.ContainerStats[chest.Id].ResolvedContentsCount);
        // Area count unchanged by attribution: chest + bin + wrench resolve to the garage.
        Assert.Equal(3, placement.AreaItemCounts[_garage.Id]);
        _ = wrench;
    }

    [Fact]
    public async Task Compute_SupplyInTwoSubBins_CountsOncePerAncestorAndArea()
    {
        var chest = await AddItemAsync("Chest", areaId: _garage.Id, isContainer: true);
        var binA = await AddItemAsync("Bin A", parentItemId: chest.Id, isContainer: true);
        var binB = await AddItemAsync("Bin B", parentItemId: chest.Id, isContainer: true);
        var screws = await AddSupplyAsync("Screws");
        await AddStockAsync(screws.Id, 10m, containerItemId: binA.Id);
        await AddStockAsync(screws.Id, 5m, containerItemId: binB.Id);

        var placement = await _sut.ComputeAsync(Ct);

        // Once per bin, once for the chest, once for the area — never double-counted.
        Assert.Equal(1, placement.ContainerStats[binA.Id].ResolvedContentsCount);
        Assert.Equal(1, placement.ContainerStats[binB.Id].ResolvedContentsCount);
        Assert.Equal(3, placement.ContainerStats[chest.Id].ResolvedContentsCount); // 2 bins + screws once
        Assert.Equal(4, placement.AreaItemCounts[_garage.Id]); // chest + 2 bins + screws once
    }

    [Fact]
    public async Task Compute_StockEntryChain_IncludesHoldingContainer()
    {
        var chest = await AddItemAsync("Chest", areaId: _garage.Id, isContainer: true);
        var screws = await AddSupplyAsync("Screws");
        await AddStockAsync(screws.Id, 10m, containerItemId: chest.Id);

        var placement = await _sut.ComputeAsync(Ct);

        // Stock IN the chest is inside the chest — the holder belongs to the entry's chain.
        Assert.Equal(1, placement.ContainerStats[chest.Id].ResolvedContentsCount);
        Assert.Equal(2, placement.AreaItemCounts[_garage.Id]); // chest + screws
    }

    [Fact]
    public async Task Compute_LowStock_ResolvesThroughChainToAreaAndContainers()
    {
        var chest = await AddItemAsync("Chest", areaId: _garage.Id, isContainer: true);
        var bin = await AddItemAsync("Bin", parentItemId: chest.Id, isContainer: true);
        var lowGlue = await AddSupplyAsync("Glue", threshold: 5m);
        await AddStockAsync(lowGlue.Id, 3m, containerItemId: bin.Id);
        var ampleTape = await AddSupplyAsync("Tape", threshold: 5m);
        await AddStockAsync(ampleTape.Id, 50m, containerItemId: bin.Id);

        var placement = await _sut.ComputeAsync(Ct);

        Assert.Equal(1, placement.AreaLowStockCounts[_garage.Id]);
        Assert.Equal(1, placement.ContainerStats[bin.Id].LowStockCount);
        Assert.Equal(1, placement.ContainerStats[chest.Id].LowStockCount);
    }

    [Fact]
    public async Task Compute_LowStock_TotalIsAcrossAllLocations()
    {
        // 3 + 4 = 7 > 5 threshold: locally scarce but globally fine ⇒ not low anywhere.
        var pantry = new Area { Id = Guid.NewGuid(), Name = "Pantry" };
        _db.Areas.Add(pantry);
        await _db.SaveChangesAsync(Ct);
        var rice = await AddSupplyAsync("Rice", threshold: 5m);
        await AddStockAsync(rice.Id, 3m, areaId: _garage.Id);
        await AddStockAsync(rice.Id, 4m, areaId: pantry.Id);

        var placement = await _sut.ComputeAsync(Ct);

        Assert.Empty(placement.AreaLowStockCounts);
    }

    [Fact]
    public async Task Compute_LowStock_ExcludesArchivedSupplies()
    {
        var archived = await AddSupplyAsync("Old glue", threshold: 5m, status: ItemStatus.Archived);
        await AddStockAsync(archived.Id, 1m, areaId: _garage.Id);
        var live = await AddSupplyAsync("Glue", threshold: 5m);
        await AddStockAsync(live.Id, 1m, areaId: _garage.Id);

        var placement = await _sut.ComputeAsync(Ct);

        Assert.Equal(1, placement.AreaLowStockCounts[_garage.Id]);
        // The archived supply's stock entry doesn't count it toward the area total either.
        Assert.Equal(1, placement.AreaItemCounts[_garage.Id]);
    }

    [Fact]
    public async Task Compute_CyclicChain_AttributesToNothing()
    {
        var binA = await AddItemAsync("Bin A", isContainer: true);
        var binB = await AddItemAsync("Bin B", parentItemId: binA.Id, isContainer: true);
        binA.ParentItemId = binB.Id; // cycle
        await _db.SaveChangesAsync(Ct);
        var wrench = await AddItemAsync("Wrench", parentItemId: binA.Id);

        var placement = await _sut.ComputeAsync(Ct);

        Assert.Empty(placement.AreaItemCounts);
        // Cycle members and the trapped item are resolved-unplaced, but attribute to no container.
        Assert.False(placement.ContainerStats.ContainsKey(binA.Id) && placement.ContainerStats[binA.Id].ResolvedContentsCount > 0);
        Assert.False(placement.ContainerStats.ContainsKey(binB.Id) && placement.ContainerStats[binB.Id].ResolvedContentsCount > 0);
        Assert.Equal(3, placement.UnplacedItems.Count);
        _ = wrench;
    }

    [Fact]
    public async Task Compute_UnplacedRootContainer_StillCountsItsContents()
    {
        var floatingBin = await AddItemAsync("Floating bin", isContainer: true);
        var nested = await AddItemAsync("Pliers", parentItemId: floatingBin.Id);

        var placement = await _sut.ComputeAsync(Ct);

        // Resolved-unplaced for areas, but drilling into the bin must still see one thing inside.
        Assert.Equal(1, placement.ContainerStats[floatingBin.Id].ResolvedContentsCount);
        Assert.Empty(placement.AreaItemCounts);
        Assert.Equal(2, placement.UnplacedItems.Count);
        _ = nested;
    }

    [Fact]
    public async Task Compute_TerminalItems_ExcludedFromContainerStats()
    {
        var chest = await AddItemAsync("Chest", areaId: _garage.Id, isContainer: true);
        await AddItemAsync("Sold drill", status: ItemStatus.Sold, parentItemId: chest.Id);
        await AddItemAsync("Archived lamp", status: ItemStatus.Archived, parentItemId: chest.Id);

        var placement = await _sut.ComputeAsync(Ct);

        Assert.False(placement.ContainerStats.ContainsKey(chest.Id));
        Assert.Equal(1, placement.AreaItemCounts[_garage.Id]); // the chest only
    }

    [Fact]
    public async Task Compute_DirectPlusNested_EqualsAreaItemCount()
    {
        // Mixed area: direct item, chest with nested item + supply, direct-area supply stock.
        var chest = await AddItemAsync("Chest", areaId: _garage.Id, isContainer: true);
        await AddItemAsync("Direct drill", areaId: _garage.Id);
        await AddItemAsync("Nested wrench", parentItemId: chest.Id);
        var screws = await AddSupplyAsync("Screws");
        await AddStockAsync(screws.Id, 10m, containerItemId: chest.Id);
        var tape = await AddSupplyAsync("Tape");
        await AddStockAsync(tape.Id, 2m, areaId: _garage.Id);

        var placement = await _sut.ComputeAsync(Ct);

        // The load-bearing invariant behind the drilled header: direct contents (chest, drill,
        // tape) + nested (wrench, screws via the chest) reconcile with the area card's count.
        var direct = 3;
        var nested = placement.ContainerStats[chest.Id].ResolvedContentsCount;
        Assert.Equal(placement.AreaItemCounts[_garage.Id], direct + nested);
        Assert.Equal(5, placement.AreaItemCounts[_garage.Id]);
    }

    [Fact]
    public async Task Compute_NestedAreas_ItemCountsRollUpToEveryAncestor()
    {
        // House › Basement › Bedroom, one item directly in each.
        var house = await AddAreaAsync("House");
        var basement = await AddAreaAsync("Basement", parentAreaId: house.Id);
        var bedroom = await AddAreaAsync("Bedroom", parentAreaId: basement.Id);
        await AddItemAsync("Painting", areaId: house.Id);
        await AddItemAsync("Boiler", areaId: basement.Id);
        await AddItemAsync("Lamp", areaId: bedroom.Id);

        var placement = await _sut.ComputeAsync(Ct);

        // The leaf keeps its own count; ancestors include all descendants additively.
        Assert.Equal(1, placement.AreaItemCounts[bedroom.Id]);
        Assert.Equal(2, placement.AreaItemCounts[basement.Id]); // boiler + lamp
        Assert.Equal(3, placement.AreaItemCounts[house.Id]);    // painting + boiler + lamp
    }

    [Fact]
    public async Task Compute_NestedAreas_SameSupplyTypeInTwoSubAreas_CountsOnceForAncestor()
    {
        // The dedup across sub-areas is load-bearing: one supply type in two children of House
        // must count once toward House, matching what the House-filtered list shows.
        var house = await AddAreaAsync("House");
        var basement = await AddAreaAsync("Basement", parentAreaId: house.Id);
        var bedroom = await AddAreaAsync("Bedroom", parentAreaId: house.Id);
        var batteries = await AddSupplyAsync("Batteries");
        await AddStockAsync(batteries.Id, 4m, areaId: basement.Id);
        await AddStockAsync(batteries.Id, 6m, areaId: bedroom.Id);

        var placement = await _sut.ComputeAsync(Ct);

        Assert.Equal(1, placement.AreaItemCounts[basement.Id]);
        Assert.Equal(1, placement.AreaItemCounts[bedroom.Id]);
        Assert.Equal(1, placement.AreaItemCounts[house.Id]); // batteries once, not twice
    }

    [Fact]
    public async Task Compute_NestedAreas_LowStockRollsUpAndDedups()
    {
        var house = await AddAreaAsync("House");
        var bedroom = await AddAreaAsync("Bedroom", parentAreaId: house.Id);
        var glue = await AddSupplyAsync("Glue", threshold: 5m);
        await AddStockAsync(glue.Id, 1m, areaId: bedroom.Id);

        var placement = await _sut.ComputeAsync(Ct);

        Assert.Equal(1, placement.AreaLowStockCounts[bedroom.Id]);
        Assert.Equal(1, placement.AreaLowStockCounts[house.Id]); // rolled up to the ancestor
    }

    [Fact]
    public async Task Compute_NestedAreas_ItemNestedInContainerRollsUpThroughAreaAncestors()
    {
        // Mixes both walks: an item in a container in Bedroom rolls up the container chain to
        // Bedroom, then up the area chain to House.
        var house = await AddAreaAsync("House");
        var bedroom = await AddAreaAsync("Bedroom", parentAreaId: house.Id);
        var chest = await AddItemAsync("Chest", areaId: bedroom.Id, isContainer: true);
        await AddItemAsync("Watch", parentItemId: chest.Id);

        var placement = await _sut.ComputeAsync(Ct);

        Assert.Equal(2, placement.AreaItemCounts[bedroom.Id]); // chest + watch
        Assert.Equal(2, placement.AreaItemCounts[house.Id]);   // rolled up
    }

    [Fact]
    public async Task Compute_AreaOwnItemCounts_ExcludeDescendantsButRollUpForAreaItemCounts()
    {
        // House (1 direct item) › Bedroom (1 direct item). Own counts are leaf-only; AreaItemCounts roll up.
        var house = await AddAreaAsync("House");
        var bedroom = await AddAreaAsync("Bedroom", parentAreaId: house.Id);
        await AddItemAsync("Painting", areaId: house.Id);
        await AddItemAsync("Lamp", areaId: bedroom.Id);

        var placement = await _sut.ComputeAsync(Ct);

        Assert.Equal(1, placement.AreaOwnItemCounts[house.Id]);    // Painting only — excludes Bedroom
        Assert.Equal(1, placement.AreaOwnItemCounts[bedroom.Id]);  // Lamp
        Assert.Equal(2, placement.AreaItemCounts[house.Id]);       // rolled: Painting + Lamp
    }

    [Fact]
    public async Task Compute_AreaOwnItemCounts_DedupSupplyTypeWithinOwnScopeOnly()
    {
        // Same supply type stocked directly in House and in a sub-area: House's OWN count includes it
        // once (its own direct entry); the sub-area's entry is NOT folded into House's own count.
        var house = await AddAreaAsync("House");
        var bedroom = await AddAreaAsync("Bedroom", parentAreaId: house.Id);
        var batteries = await AddSupplyAsync("Batteries");
        await AddStockAsync(batteries.Id, 4m, areaId: house.Id);
        await AddStockAsync(batteries.Id, 6m, areaId: bedroom.Id);

        var placement = await _sut.ComputeAsync(Ct);

        Assert.Equal(1, placement.AreaOwnItemCounts[house.Id]);   // Batteries once (House's own entry)
        Assert.Equal(1, placement.AreaOwnItemCounts[bedroom.Id]); // Batteries once
        Assert.Equal(1, placement.AreaItemCounts[house.Id]);      // rolled, still deduped to once
    }

    [Fact]
    public async Task Compute_Lent_DirectlyPlaced_CountsTowardAreaAsSubsetOfItems()
    {
        await AddItemAsync("Lent ladder", status: ItemStatus.Lent, areaId: _garage.Id);
        await AddItemAsync("Owned drill", areaId: _garage.Id); // Active — not lent

        var placement = await _sut.ComputeAsync(Ct);

        Assert.Equal(1, placement.AreaLentCounts[_garage.Id]);
        // Lent is a subset of the area's items — both the ladder and the drill count toward the total.
        Assert.Equal(2, placement.AreaItemCounts[_garage.Id]);
    }

    [Fact]
    public async Task Compute_Lent_Nested_ResolvesThroughChainToAreaAndContainers()
    {
        var chest = await AddItemAsync("Chest", areaId: _garage.Id, isContainer: true);
        var bin = await AddItemAsync("Bin", parentItemId: chest.Id, isContainer: true);
        await AddItemAsync("Lent wrench", status: ItemStatus.Lent, parentItemId: bin.Id);

        var placement = await _sut.ComputeAsync(Ct);

        // Same resolved walk as the item count: attributes to the area + every container in the chain.
        Assert.Equal(1, placement.AreaLentCounts[_garage.Id]);
        Assert.Equal(1, placement.ContainerStats[bin.Id].LentCount);
        Assert.Equal(1, placement.ContainerStats[chest.Id].LentCount);
    }

    [Fact]
    public async Task Compute_Lent_OnlyLentStatusCounts()
    {
        // Only Lent counts toward the lent tally — neither other live statuses nor terminal ones do.
        await AddItemAsync("Owned drill", areaId: _garage.Id);
        await AddItemAsync("In-repair saw", status: ItemStatus.InRepair, areaId: _garage.Id);
        await AddItemAsync("Sold lamp", status: ItemStatus.Sold, areaId: _garage.Id);

        var placement = await _sut.ComputeAsync(Ct);

        Assert.False(placement.AreaLentCounts.ContainsKey(_garage.Id));
    }

    [Fact]
    public async Task Compute_Lent_RollsUpAreaAncestors()
    {
        var house = await AddAreaAsync("House");
        var bedroom = await AddAreaAsync("Bedroom", parentAreaId: house.Id);
        await AddItemAsync("Lent book", status: ItemStatus.Lent, areaId: bedroom.Id);

        var placement = await _sut.ComputeAsync(Ct);

        Assert.Equal(1, placement.AreaLentCounts[bedroom.Id]);
        Assert.Equal(1, placement.AreaLentCounts[house.Id]); // rolled up to the ancestor
    }

    [Fact]
    public async Task Compute_Lent_UnplacedRootContainer_StillCountsTowardThatContainer()
    {
        // A lent item in an unplaced container resolves to no AREA, but still counts toward the
        // container itself — mirrors the item walk (Compute_UnplacedRootContainer_StillCountsItsContents),
        // so drilling into that floating bin shows one lent thing inside.
        var floatingBin = await AddItemAsync("Floating bin", isContainer: true);
        await AddItemAsync("Lent torch", status: ItemStatus.Lent, parentItemId: floatingBin.Id);

        var placement = await _sut.ComputeAsync(Ct);

        Assert.Empty(placement.AreaLentCounts);
        Assert.Equal(1, placement.ContainerStats[floatingBin.Id].LentCount);
    }

    [Fact]
    public async Task Compute_AreaParentCycle_DoesNotLoop()
    {
        // A manually-introduced area cycle (House ↔ Basement) must terminate, not hang.
        var house = await AddAreaAsync("House");
        var basement = await AddAreaAsync("Basement", parentAreaId: house.Id);
        house.ParentAreaId = basement.Id;
        await _db.SaveChangesAsync(Ct);
        await AddItemAsync("Lamp", areaId: basement.Id);

        var placement = await _sut.ComputeAsync(Ct);

        // The cycle guard caps the climb at the visited set — both members get the count, once.
        Assert.Equal(1, placement.AreaItemCounts[basement.Id]);
        Assert.Equal(1, placement.AreaItemCounts[house.Id]);
    }
}
