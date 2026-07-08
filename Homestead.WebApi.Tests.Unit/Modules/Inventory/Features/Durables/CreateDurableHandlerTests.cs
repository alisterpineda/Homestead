using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Durables;
using Homestead.WebApi.Modules.Inventory.Features.Durables.CreateDurable;
using Homestead.WebApi.Modules.Inventory.Services;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Features.Durables;

public class CreateDurableHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly CreateDurableHandler _sut;

    public CreateDurableHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _currentUser = Substitute.For<ICurrentUserAccessor>();
        _currentUser.UserId.Returns(Guid.NewGuid());
        _audit = Substitute.For<IAuditContext>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));
        _sut = new CreateDurableHandler(_db, _currentUser, _audit, new ItemLocationResolver(_db), new InventoryTagPathResolver(_db), _unitOfWork);
    }

    private async Task<Item> AddContainerAsync(Guid? areaId = null, DurableKind durableKind = DurableKind.Container)
    {
        var container = new Item
        {
            Id = Guid.NewGuid(),
            Name = "Tool chest",
            Fungibility = Fungibility.NonFungible,
            Status = ItemStatus.Active,
            DurableKind = durableKind,
            AreaId = areaId,
        };
        _db.Items.Add(container);
        await _db.SaveChangesAsync(Ct);
        return container;
    }

    public void Dispose() => _db.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Commodity> AddCurrencyAsync(string symbol = "CAD")
    {
        var c = new Commodity { Id = Guid.NewGuid(), Symbol = symbol, Name = symbol, Kind = CommodityKind.Currency, DisplayPrecision = 2 };
        _db.Commodities.Add(c);
        await _db.SaveChangesAsync(Ct);
        return c;
    }

    private async Task<Brand> AddBrandAsync(string name = "Apple")
    {
        var b = new Brand { Id = Guid.NewGuid(), Name = name };
        _db.Brands.Add(b);
        await _db.SaveChangesAsync(Ct);
        return b;
    }

    [Fact]
    public async Task Handle_AppliesNonFungibleActiveDefaults()
    {
        var result = await _sut.Handle(new CreateDurableCommand { Name = "Drill" }, Ct);

        Assert.Equal("Drill", result.Name);
        Assert.Equal(Fungibility.NonFungible, result.Fungibility);
        Assert.Equal(ItemStatus.Active, result.Status);
        Assert.Equal(DurableKind.Plain, result.DurableKind);
        Assert.Null(result.AreaId);

        var stored = _db.Items.Single();
        Assert.Equal(Fungibility.NonFungible, stored.Fungibility);
        Assert.Equal(ItemStatus.Active, stored.Status);
        Assert.Equal(DurableKind.Plain, stored.DurableKind);
    }

    [Fact]
    public async Task Handle_LogsCreatedAudit()
    {
        var result = await _sut.Handle(new CreateDurableCommand { Name = "Drill" }, Ct);

        _audit.Received(1).Created("Item", result.Id, Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_WithKnownArea_PlacesInArea()
    {
        var area = new Area { Id = Guid.NewGuid(), Name = "Garage" };
        _db.Areas.Add(area);
        await _db.SaveChangesAsync(Ct);

        var result = await _sut.Handle(new CreateDurableCommand { Name = "Drill", AreaId = area.Id }, Ct);

        Assert.Equal(area.Id, result.AreaId);
        Assert.Equal("Garage", result.AreaName);
    }

    [Fact]
    public async Task Handle_WithUnknownArea_Throws()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.Handle(new CreateDurableCommand { Name = "Drill", AreaId = Guid.NewGuid() }, Ct));
    }

    [Fact]
    public async Task Handle_WithValueButNoCurrency_Throws()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.Handle(new CreateDurableCommand { Name = "Drill", Value = 100m }, Ct));
    }

    [Fact]
    public async Task Handle_WithNonCurrencyCommodity_Throws()
    {
        var security = new Commodity { Id = Guid.NewGuid(), Symbol = "AAPL", Exchange = "NASDAQ", Name = "Apple", Kind = CommodityKind.Security, DisplayPrecision = 2 };
        _db.Commodities.Add(security);
        await _db.SaveChangesAsync(Ct);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.Handle(new CreateDurableCommand { Name = "Drill", Value = 100m, ValueCurrencyId = security.Id }, Ct));
    }

    [Fact]
    public async Task Handle_WithValueAndCurrency_StoresValueAndSymbol()
    {
        var cad = await AddCurrencyAsync();

        var result = await _sut.Handle(
            new CreateDurableCommand { Name = "Camera", Value = 1200m, ValueCurrencyId = cad.Id }, Ct);

        Assert.Equal(1200m, result.Value);
        Assert.Equal(cad.Id, result.ValueCurrencyId);
        Assert.Equal("CAD", result.ValueCurrencySymbol);
    }

    [Fact]
    public async Task Handle_WithContainerKind_CreatesContainer()
    {
        var result = await _sut.Handle(
            new CreateDurableCommand { Name = "Tool chest", DurableKind = DurableKind.Container }, Ct);

        Assert.Equal(DurableKind.Container, result.DurableKind);
        Assert.Equal(DurableKind.Container, _db.Items.Single().DurableKind);
    }

    [Fact]
    public async Task Handle_WithAssemblyKind_CreatesAssembly()
    {
        var result = await _sut.Handle(
            new CreateDurableCommand { Name = "Gaming PC", DurableKind = DurableKind.Assembly }, Ct);

        Assert.Equal(DurableKind.Assembly, result.DurableKind);
        Assert.Equal(DurableKind.Assembly, _db.Items.Single().DurableKind);
    }

    [Fact]
    public async Task Handle_IntoAssemblyWithCompartment_SetsComponentLabelAndParentKind()
    {
        var area = new Area { Id = Guid.NewGuid(), Name = "Garage" };
        _db.Areas.Add(area);
        await _db.SaveChangesAsync(Ct);
        var pc = await AddContainerAsync(areaId: area.Id, durableKind: DurableKind.Assembly);

        var result = await _sut.Handle(
            new CreateDurableCommand { Name = "CPU", ParentItemId = pc.Id, Compartment = "CPU" }, Ct);

        Assert.Equal(pc.Id, result.ParentItemId);
        Assert.Equal(DurableKind.Assembly, result.ParentDurableKind);
        Assert.Equal("CPU", result.Compartment);
        // A component resolves its area by walking up through the assembly.
        Assert.Equal(area.Id, result.ResolvedAreaId);
    }

    [Fact]
    public async Task Handle_InContainerWithCompartment_PlacesAndResolvesPath()
    {
        var area = new Area { Id = Guid.NewGuid(), Name = "Garage" };
        _db.Areas.Add(area);
        await _db.SaveChangesAsync(Ct);
        var chest = await AddContainerAsync(areaId: area.Id);

        var result = await _sut.Handle(
            new CreateDurableCommand { Name = "Wrench", ParentItemId = chest.Id, Compartment = "Top drawer" }, Ct);

        Assert.Equal(chest.Id, result.ParentItemId);
        Assert.Null(result.AreaId);
        Assert.Equal("Top drawer", result.Compartment);
        // Path resolves root→immediate-parent: Garage(Area) → Tool chest(Container).
        Assert.Equal(area.Id, result.ResolvedAreaId);
        Assert.Equal(2, result.Path.Count);
        Assert.Equal(area.Id, result.Path[0].Id);
        Assert.Equal(LocationSegmentKind.Area, result.Path[0].Kind);
        Assert.Equal(chest.Id, result.Path[1].Id);
        Assert.Equal(LocationSegmentKind.Container, result.Path[1].Kind);
    }

    [Fact]
    public async Task Handle_WithUnknownBrand_Throws()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.Handle(new CreateDurableCommand { Name = "MacBook", BrandId = Guid.NewGuid() }, Ct));
    }

    [Fact]
    public async Task Handle_WithKnownBrand_MapsBrandIdAndName()
    {
        var apple = await AddBrandAsync();

        var result = await _sut.Handle(
            new CreateDurableCommand { Name = "MacBook", BrandId = apple.Id }, Ct);

        Assert.Equal(apple.Id, result.BrandId);
        Assert.Equal("Apple", result.BrandName);
        Assert.Equal(apple.Id, _db.Items.Single().BrandId);
    }

    [Fact]
    public async Task Handle_WithModel_TrimsAndStoresModel()
    {
        var result = await _sut.Handle(
            new CreateDurableCommand { Name = "MacBook", Model = "  MRW23  " }, Ct);

        Assert.Equal("MRW23", result.Model);
        Assert.Equal("MRW23", _db.Items.Single().Model);
    }

    [Fact]
    public async Task Handle_WithBlankModel_StoresNull()
    {
        var result = await _sut.Handle(
            new CreateDurableCommand { Name = "MacBook", Model = "   " }, Ct);

        Assert.Null(result.Model);
        Assert.Null(_db.Items.Single().Model);
    }

    [Fact]
    public async Task Handle_WithMissingParent_Throws()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.Handle(new CreateDurableCommand { Name = "Wrench", ParentItemId = Guid.NewGuid() }, Ct));
    }

    [Fact]
    public async Task Handle_WithTags_ResolvesOrCreatesTagsAndPopulatesResponse()
    {
        var result = await _sut.Handle(
            new CreateDurableCommand { Name = "Drill", Tags = ["tools/power"] }, Ct);

        // Ancestors upserted: both "tools" and "tools/power" exist.
        Assert.Equal(2, _db.InventoryTags.Count());
        Assert.Contains(_db.InventoryTags, t => t.Path == "tools");
        var leaf = _db.InventoryTags.Single(t => t.Path == "tools/power");

        // The created item carries the leaf ItemTag join row (not the ancestor).
        var itemTags = _db.ItemTags.Where(it => it.ItemId == result.Id).ToList();
        Assert.Single(itemTags);
        Assert.Equal(leaf.Id, itemTags[0].TagId);

        // The response .Tags projection is populated with the leaf.
        Assert.Single(result.Tags);
        Assert.Equal("tools/power", result.Tags[0].Path);
    }

    [Fact]
    public async Task Handle_WithNoTags_LeavesItemUntagged()
    {
        var result = await _sut.Handle(new CreateDurableCommand { Name = "Drill" }, Ct);

        Assert.Empty(result.Tags);
        Assert.Empty(_db.ItemTags.Where(it => it.ItemId == result.Id));
        Assert.Empty(_db.InventoryTags);
    }

    [Fact]
    public async Task Handle_WithParentNotContainer_Throws()
    {
        var plain = new Item
        {
            Id = Guid.NewGuid(),
            Name = "Hammer",
            Fungibility = Fungibility.NonFungible,
            Status = ItemStatus.Active,
            DurableKind = DurableKind.Plain,
        };
        _db.Items.Add(plain);
        await _db.SaveChangesAsync(Ct);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.Handle(new CreateDurableCommand { Name = "Wrench", ParentItemId = plain.Id }, Ct));
    }
}
