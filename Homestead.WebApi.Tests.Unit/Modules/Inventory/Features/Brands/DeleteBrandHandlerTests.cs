using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Brands.DeleteBrand;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Features.Brands;

public class DeleteBrandHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IAuditContext _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly DeleteBrandHandler _sut;

    public DeleteBrandHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _audit = Substitute.For<IAuditContext>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));
        _sut = new DeleteBrandHandler(_db, _audit, _unitOfWork);
    }

    public void Dispose() => _db.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Brand> AddBrandAsync(string name)
    {
        var brand = new Brand { Id = Guid.NewGuid(), Name = name };
        _db.Brands.Add(brand);
        await _db.SaveChangesAsync(Ct);
        return brand;
    }

    private async Task AddItemWithBrandAsync(Guid brandId)
    {
        _db.Items.Add(new Item
        {
            Id = Guid.NewGuid(),
            Name = "iPhone",
            Fungibility = Fungibility.NonFungible,
            Status = ItemStatus.Active,
            BrandId = brandId,
        });
        await _db.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task Handle_UnusedBrand_DeletesAndAudits()
    {
        var brand = await AddBrandAsync("Apple");

        var result = await _sut.Handle(new DeleteBrandCommand(brand.Id), Ct);

        Assert.True(result);
        Assert.Empty(_db.Brands);
        _audit.Received(1).Deleted("Brand", brand.Id, "Apple", Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_InUseBrand_ThrowsAndKeepsRow()
    {
        var brand = await AddBrandAsync("Apple");
        await AddItemWithBrandAsync(brand.Id);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.Handle(new DeleteBrandCommand(brand.Id), Ct));

        Assert.Contains(_db.Brands, b => b.Id == brand.Id);
        _audit.DidNotReceive().Deleted("Brand", Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_MissingBrand_ReturnsFalse()
    {
        var result = await _sut.Handle(new DeleteBrandCommand(Guid.NewGuid()), Ct);

        Assert.False(result);
    }
}
