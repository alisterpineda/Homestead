using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Items;
using Homestead.WebApi.Modules.Inventory.Features.Items.CreateItem;
using Homestead.WebApi.Modules.Inventory.Services;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Features.Items;

public class CreateItemHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly CreateItemHandler _sut;

    public CreateItemHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _currentUser = Substitute.For<ICurrentUserAccessor>();
        _currentUser.UserId.Returns(Guid.NewGuid());
        _audit = Substitute.For<IAuditContext>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));
        _sut = new CreateItemHandler(_db, _currentUser, _audit, new ItemLocationResolver(_db), _unitOfWork);
    }

    private async Task<Item> AddContainerAsync(Guid? areaId = null)
    {
        var container = new Item
        {
            Id = Guid.NewGuid(),
            Name = "Tool chest",
            Fungibility = Fungibility.NonFungible,
            Status = ItemStatus.Active,
            IsContainer = true,
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

    [Fact]
    public async Task Handle_AppliesNonFungibleActiveDefaults()
    {
        var result = await _sut.Handle(new CreateItemCommand { Name = "Drill" }, Ct);

        Assert.Equal("Drill", result.Name);
        Assert.Equal(Fungibility.NonFungible, result.Fungibility);
        Assert.Equal(ItemStatus.Active, result.Status);
        Assert.False(result.IsContainer);
        Assert.Null(result.AreaId);

        var stored = _db.Items.Single();
        Assert.Equal(Fungibility.NonFungible, stored.Fungibility);
        Assert.Equal(ItemStatus.Active, stored.Status);
        Assert.False(stored.IsContainer);
    }

    [Fact]
    public async Task Handle_LogsCreatedAudit()
    {
        var result = await _sut.Handle(new CreateItemCommand { Name = "Drill" }, Ct);

        _audit.Received(1).Created("Item", result.Id, Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_WithKnownArea_PlacesInArea()
    {
        var area = new Area { Id = Guid.NewGuid(), Name = "Garage" };
        _db.Areas.Add(area);
        await _db.SaveChangesAsync(Ct);

        var result = await _sut.Handle(new CreateItemCommand { Name = "Drill", AreaId = area.Id }, Ct);

        Assert.Equal(area.Id, result.AreaId);
        Assert.Equal("Garage", result.AreaName);
    }

    [Fact]
    public async Task Handle_WithUnknownArea_Throws()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.Handle(new CreateItemCommand { Name = "Drill", AreaId = Guid.NewGuid() }, Ct));
    }

    [Fact]
    public async Task Handle_WithValueButNoCurrency_Throws()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.Handle(new CreateItemCommand { Name = "Drill", Value = 100m }, Ct));
    }

    [Fact]
    public async Task Handle_WithNonCurrencyCommodity_Throws()
    {
        var security = new Commodity { Id = Guid.NewGuid(), Symbol = "AAPL", Exchange = "NASDAQ", Name = "Apple", Kind = CommodityKind.Security, DisplayPrecision = 2 };
        _db.Commodities.Add(security);
        await _db.SaveChangesAsync(Ct);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.Handle(new CreateItemCommand { Name = "Drill", Value = 100m, ValueCurrencyId = security.Id }, Ct));
    }

    [Fact]
    public async Task Handle_WithValueAndCurrency_StoresValueAndSymbol()
    {
        var cad = await AddCurrencyAsync();

        var result = await _sut.Handle(
            new CreateItemCommand { Name = "Camera", Value = 1200m, ValueCurrencyId = cad.Id }, Ct);

        Assert.Equal(1200m, result.Value);
        Assert.Equal(cad.Id, result.ValueCurrencyId);
        Assert.Equal("CAD", result.ValueCurrencySymbol);
    }

    [Fact]
    public async Task Handle_WithIsContainer_CreatesContainer()
    {
        var result = await _sut.Handle(new CreateItemCommand { Name = "Tool chest", IsContainer = true }, Ct);

        Assert.True(result.IsContainer);
        Assert.True(_db.Items.Single().IsContainer);
    }

    [Fact]
    public async Task Handle_InContainerWithCompartment_PlacesAndResolvesPath()
    {
        var area = new Area { Id = Guid.NewGuid(), Name = "Garage" };
        _db.Areas.Add(area);
        await _db.SaveChangesAsync(Ct);
        var chest = await AddContainerAsync(areaId: area.Id);

        var result = await _sut.Handle(
            new CreateItemCommand { Name = "Wrench", ParentItemId = chest.Id, Compartment = "Top drawer" }, Ct);

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
    public async Task Handle_WithMissingParent_Throws()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.Handle(new CreateItemCommand { Name = "Wrench", ParentItemId = Guid.NewGuid() }, Ct));
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
            IsContainer = false,
        };
        _db.Items.Add(plain);
        await _db.SaveChangesAsync(Ct);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.Handle(new CreateItemCommand { Name = "Wrench", ParentItemId = plain.Id }, Ct));
    }
}
