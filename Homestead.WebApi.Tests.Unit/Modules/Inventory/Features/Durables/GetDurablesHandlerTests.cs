using Homestead.WebApi.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
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
        Guid? parentItemId = null, bool isContainer = false, Guid? brandId = null)
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

    private async Task<Tag> AddTagAsync(string name, string path, Guid? parentId = null)
    {
        var tag = new Tag { Id = Guid.NewGuid(), Name = name, Path = path, ParentId = parentId };
        _db.InventoryTags.Add(tag);
        await _db.SaveChangesAsync(Ct);
        return tag;
    }

    private async Task TagItemAsync(Guid itemId, Guid tagId)
    {
        _db.ItemTags.Add(new ItemTag { ItemId = itemId, TagId = tagId });
        await _db.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task Handle_FiltersByTagIds_NarrowsToTaggedItems()
    {
        var electronics = await AddTagAsync("electronics", "electronics");
        var garden = await AddTagAsync("garden", "garden");
        var tv = await AddItemAsync("Bravia TV");
        await TagItemAsync(tv.Id, electronics.Id);
        var mower = await AddItemAsync("Lawn mower");
        await TagItemAsync(mower.Id, garden.Id);
        await AddItemAsync("Untagged drill");

        var resp = await _sut.HandleAsync(
            new GetDurablesQuery { Page = 1, PageSize = 50, TagIds = [electronics.Id] }, Ct);

        Assert.Single(resp.Items);
        Assert.Equal("Bravia TV", resp.Items[0].Name);
        // The projection populates Tags on returned rows.
        Assert.Single(resp.Items[0].Tags);
        Assert.Equal("electronics", resp.Items[0].Tags[0].Path);
    }

    [Fact]
    public async Task Handle_FiltersByTagIds_IncludeChildTags_MatchesDescendantTag()
    {
        var tools = await AddTagAsync("tools", "tools");
        var power = await AddTagAsync("power", "tools/power", tools.Id);
        var drill = await AddItemAsync("Cordless drill");
        await TagItemAsync(drill.Id, power.Id); // tagged with the CHILD tag only

        // Without descendant expansion, filtering on the parent tag matches nothing.
        var narrow = await _sut.HandleAsync(
            new GetDurablesQuery { Page = 1, PageSize = 50, TagIds = [tools.Id], IncludeChildTags = false }, Ct);
        Assert.Empty(narrow.Items);

        // With IncludeChildTags, the parent filter rolls in items tagged with any descendant.
        var expanded = await _sut.HandleAsync(
            new GetDurablesQuery { Page = 1, PageSize = 50, TagIds = [tools.Id], IncludeChildTags = true }, Ct);
        Assert.Single(expanded.Items);
        Assert.Equal("Cordless drill", expanded.Items[0].Name);
    }

    [Fact]
    public async Task Handle_TagFilter_ComposesWithAreaFilter()
    {
        var electronics = await AddTagAsync("electronics", "electronics");
        var garageTv = await AddItemAsync("Garage TV", areaId: _garage.Id);
        await TagItemAsync(garageTv.Id, electronics.Id);
        var officeTv = await AddItemAsync("Office TV", areaId: _office.Id);
        await TagItemAsync(officeTv.Id, electronics.Id);

        var resp = await _sut.HandleAsync(
            new GetDurablesQuery { Page = 1, PageSize = 50, TagIds = [electronics.Id], AreaId = _garage.Id }, Ct);

        // Both are tagged, but only the garage one satisfies the composed area filter.
        Assert.Single(resp.Items);
        Assert.Equal("Garage TV", resp.Items[0].Name);
    }

    [Fact]
    public async Task Handle_FiltersByBrand_NarrowsToBrand()
    {
        var apple = await AddBrandAsync("Apple");
        var sony = await AddBrandAsync("Sony");
        await AddItemAsync("MacBook", brandId: apple.Id);
        await AddItemAsync("Bravia TV", brandId: sony.Id);
        await AddItemAsync("Unbranded drill");

        var resp = await _sut.HandleAsync(new GetDurablesQuery { Page = 1, PageSize = 50, BrandId = apple.Id }, Ct);

        Assert.Single(resp.Items);
        Assert.Equal("MacBook", resp.Items[0].Name);
        Assert.Equal(apple.Id, resp.Items[0].BrandId);
        Assert.Equal("Apple", resp.Items[0].BrandName);
    }

    [Fact]
    public async Task Handle_HidesTerminalItemsByDefault()
    {
        await AddItemAsync("Drill");
        await AddItemAsync("Archived box", ItemStatus.Archived);

        var resp = await _sut.HandleAsync(new GetDurablesQuery { Page = 1, PageSize = 50 }, Ct);

        Assert.Single(resp.Items);
        Assert.Equal("Drill", resp.Items[0].Name);
    }

    [Fact]
    public async Task Handle_IncludeInactive_ShowsTerminalItems()
    {
        await AddItemAsync("Drill");
        await AddItemAsync("Old TV", ItemStatus.Archived);

        var resp = await _sut.HandleAsync(new GetDurablesQuery { Page = 1, PageSize = 50, IncludeInactive = true }, Ct);

        Assert.Equal(2, resp.Items.Count);
    }

    [Fact]
    public async Task Handle_FiltersByArea_MatchesDirectlyPlaced()
    {
        await AddItemAsync("Drill", areaId: _garage.Id);
        await AddItemAsync("Stapler", areaId: _office.Id);

        var resp = await _sut.HandleAsync(new GetDurablesQuery { Page = 1, PageSize = 50, AreaId = _garage.Id }, Ct);

        Assert.Single(resp.Items);
        Assert.Equal("Drill", resp.Items[0].Name);
        Assert.Equal("Garage", resp.Items[0].AreaName);
    }

    [Fact]
    public async Task Handle_FiltersByArea_MatchesResolvedNotJustDirect()
    {
        // Wrench nested in a chest that sits in the garage — resolves to garage, not directly placed.
        var chest = await AddItemAsync("Tool chest", areaId: _garage.Id, isContainer: true);
        await AddItemAsync("Wrench", parentItemId: chest.Id);
        await AddItemAsync("Stapler", areaId: _office.Id);

        var resp = await _sut.HandleAsync(new GetDurablesQuery { Page = 1, PageSize = 50, AreaId = _garage.Id }, Ct);

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

        var resp = await _sut.HandleAsync(new GetDurablesQuery { Page = 1, PageSize = 50, AreaId = _garage.Id }, Ct);

        // Filtering the parent area rolls in items resolved to its descendant areas.
        Assert.Single(resp.Items);
        Assert.Equal("Vise", resp.Items[0].Name);
    }

    [Fact]
    public async Task Handle_FiltersUnplaced_MatchesDirectlyUnplaced()
    {
        await AddItemAsync("Drill", areaId: _garage.Id);
        await AddItemAsync("Floating thing");

        var resp = await _sut.HandleAsync(new GetDurablesQuery { Page = 1, PageSize = 50, Unplaced = true }, Ct);

        Assert.Single(resp.Items);
        Assert.Equal("Floating thing", resp.Items[0].Name);
        Assert.Null(resp.Items[0].AreaId);
    }

    [Fact]
    public async Task Handle_FiltersUnplaced_MatchesBrokenContainerChain()
    {
        // A container with no area holding a screw → the screw resolves to no area (unplaced).
        var floatingBin = await AddItemAsync("Floating bin", isContainer: true);
        await AddItemAsync("Screw", parentItemId: floatingBin.Id);
        await AddItemAsync("Drill", areaId: _garage.Id);

        var resp = await _sut.HandleAsync(new GetDurablesQuery { Page = 1, PageSize = 50, Unplaced = true }, Ct);

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

        var resp = await _sut.HandleAsync(new GetDurablesQuery { Page = 1, PageSize = 2, AreaId = _garage.Id }, Ct);

        Assert.Equal(2, resp.Items.Count); // one page
        Assert.Equal(5, resp.TotalCount);  // whole filtered set, not the page or the office item
    }

    [Fact]
    public async Task Handle_FiltersBySearch()
    {
        await AddItemAsync("Cordless drill");
        await AddItemAsync("Stapler");

        var resp = await _sut.HandleAsync(new GetDurablesQuery { Page = 1, PageSize = 50, Search = "drill" }, Ct);

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

        var resp = await _sut.HandleAsync(
            new GetDurablesQuery { Page = 1, PageSize = 50, ParentItemId = chest.Id }, Ct);

        Assert.Equal(2, resp.Items.Count); // bin + wrench, not the grandchild
        Assert.Contains(resp.Items, i => i.Name == "Bin");
        Assert.Contains(resp.Items, i => i.Name == "Wrench");
        Assert.DoesNotContain(resp.Items, i => i.Name == "Screw driver");
    }

    // ----- Derived warranty status (the item-level rule lives in DurableResponseMapper's SQL) -----

    private async Task AddWarrantyAsync(Guid itemId, DateOnly? expiresOn)
    {
        _db.ItemWarranties.Add(new ItemWarranty
        {
            Id = Guid.NewGuid(),
            ItemId = itemId,
            ExpiresOn = expiresOn,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task Handle_NoWarranties_IsNone()
    {
        await AddItemAsync("Hammer");

        var resp = await _sut.HandleAsync(new GetDurablesQuery { Page = 1, PageSize = 50 }, Ct);

        Assert.Equal(WarrantyStatus.None, Assert.Single(resp.Items).WarrantyStatus);
    }

    [Theory]
    [InlineData(-1, WarrantyStatus.Expired)]
    [InlineData(0, WarrantyStatus.Expiring)]
    [InlineData(WarrantyStatusRules.ExpiringWindowDays, WarrantyStatus.Expiring)]
    [InlineData(WarrantyStatusRules.ExpiringWindowDays + 1, WarrantyStatus.Active)]
    public async Task Projection_DerivesStatusFromTheLatestExpiry(int offsetDays, WarrantyStatus expected)
    {
        // Pinned through For(today) rather than the handler's wall-clock Projection: the edge cases
        // (0 and +30) would otherwise flip if the run straddled UTC midnight between seed and query.
        var today = new DateOnly(2026, 3, 1);
        var item = await AddItemAsync("Dryer");
        // An older, already-lapsed tier must not drag the verdict down: the latest expiry decides.
        await AddWarrantyAsync(item.Id, today.AddDays(-400));
        await AddWarrantyAsync(item.Id, today.AddDays(offsetDays));

        var projected = await _db.Items
            .Where(i => i.Id == item.Id)
            .Select(Homestead.WebApi.Modules.Inventory.Features.Durables.DurableResponseMapper.For(today))
            .SingleAsync(Ct);

        Assert.Equal(expected, projected.WarrantyStatus);
    }

    [Fact]
    public async Task Handle_OpenEndedRow_WinsOverAnExpiredOne()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var item = await AddItemAsync("Cast iron pan");
        await AddWarrantyAsync(item.Id, today.AddDays(-400));
        await AddWarrantyAsync(item.Id, null);

        var resp = await _sut.HandleAsync(new GetDurablesQuery { Page = 1, PageSize = 50 }, Ct);

        Assert.Equal(WarrantyStatus.Active, Assert.Single(resp.Items).WarrantyStatus);
    }

    [Fact]
    public async Task Projection_FungibleItem_IsNone()
    {
        // Warranties are durables-only, so a consumable can never have rows — the list excludes it,
        // but GetItem projects every kind through this same expression, so pin the value here.
        var consumable = new Item
        {
            Id = Guid.NewGuid(), Name = "Screws", Fungibility = Fungibility.Fungible,
            Status = ItemStatus.Active,
        };
        _db.Items.Add(consumable);
        await _db.SaveChangesAsync(Ct);

        var projected = await _db.Items
            .Where(i => i.Id == consumable.Id)
            .Select(Homestead.WebApi.Modules.Inventory.Features.Durables.DurableResponseMapper
                .For(DateOnly.FromDateTime(DateTime.UtcNow)))
            .SingleAsync(Ct);

        Assert.Equal(WarrantyStatus.None, projected.WarrantyStatus);
    }

    [Fact]
    public async Task Handle_IncludesComponentsAndContainers()
    {
        // No kind filter exists on the list; a non-container assembly, its component, and a container
        // all surface as first-class rows.
        var pc = await AddItemAsync("Gaming PC", areaId: _garage.Id, isContainer: false);
        await AddItemAsync("CPU", parentItemId: pc.Id); // a component is first-class in the list
        await AddItemAsync("Tool chest", areaId: _garage.Id, isContainer: true);

        var resp = await _sut.HandleAsync(new GetDurablesQuery { Page = 1, PageSize = 50 }, Ct);

        Assert.Equal(3, resp.Items.Count);
        Assert.Contains(resp.Items, i => i.Name == "CPU");
        Assert.Contains(resp.Items, i => i.Name == "Gaming PC");
        Assert.Contains(resp.Items, i => i.Name == "Tool chest");
    }
}
