using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Items.CreateItem;
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
        _sut = new CreateItemHandler(_db, _currentUser, _audit, _unitOfWork);
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
}
