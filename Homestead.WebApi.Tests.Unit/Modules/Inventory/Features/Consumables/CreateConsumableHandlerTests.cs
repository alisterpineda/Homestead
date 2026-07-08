using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Consumables.CreateConsumable;
using Homestead.WebApi.Modules.Inventory.Services;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Features.Consumables;

public class CreateConsumableHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IAuditContext _audit;
    private readonly IStockService _stock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly CreateConsumableHandler _sut;

    public CreateConsumableHandlerTests()
    {
        _db = TestAppDbContext.Create();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(Guid.NewGuid());
        _audit = Substitute.For<IAuditContext>();
        _stock = Substitute.For<IStockService>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));
        _sut = new CreateConsumableHandler(_db, currentUser, _audit, _stock, _unitOfWork);
    }

    public void Dispose() => _db.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Brand> AddBrandAsync(string name = "Apple")
    {
        var b = new Brand { Id = Guid.NewGuid(), Name = name };
        _db.Brands.Add(b);
        await _db.SaveChangesAsync(Ct);
        return b;
    }

    [Fact]
    public async Task Handle_AppliesFungibleActiveDefaults()
    {
        var result = await _sut.Handle(new CreateConsumableCommand { Name = "Wood screws", Unit = "pcs" }, Ct);

        Assert.Equal("Wood screws", result.Name);
        Assert.Equal(ItemStatus.Active, result.Status);
        Assert.Equal(Fungibility.Fungible, _db.Items.Single().Fungibility);
    }

    [Fact]
    public async Task Handle_WithUnknownBrand_Throws()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.Handle(new CreateConsumableCommand { Name = "Wood screws", BrandId = Guid.NewGuid() }, Ct));

        Assert.Empty(_db.Items);
    }

    [Fact]
    public async Task Handle_WithKnownBrand_MapsBrandIdAndName()
    {
        var apple = await AddBrandAsync();

        var result = await _sut.Handle(
            new CreateConsumableCommand { Name = "Lightning cables", BrandId = apple.Id }, Ct);

        Assert.Equal(apple.Id, result.BrandId);
        Assert.Equal("Apple", result.BrandName);
        Assert.Equal(apple.Id, _db.Items.Single().BrandId);
    }
}
