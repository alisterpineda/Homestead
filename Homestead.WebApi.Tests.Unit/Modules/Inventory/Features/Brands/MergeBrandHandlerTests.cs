using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Brands.MergeBrand;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Features.Brands;

/// <summary>
/// Unit coverage is limited to the guard paths that reject before the bulk re-point. The re-point
/// itself uses <c>ExecuteUpdateAsync</c> (DeleteGroupHandler precedent), which the EF InMemory
/// provider cannot execute — so the "items re-point source→target, source removed, target survives,
/// deletion audited" assertions live in <c>BrandsEndpointTests</c> against real Postgres, mirroring
/// how DeleteGroupHandler's bulk null-out is covered only at the integration level.
/// </summary>
public class MergeBrandHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IAuditContext _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly MergeBrandHandler _sut;

    public MergeBrandHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _audit = Substitute.For<IAuditContext>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));
        _sut = new MergeBrandHandler(_db, _audit, _unitOfWork);
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

    [Fact]
    public async Task Handle_SourceEqualsTarget_ThrowsBusinessRule()
    {
        var brand = await AddBrandAsync("Apple");

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.HandleAsync(brand.Id, new MergeBrandRequest(brand.Id), Ct));
    }

    [Fact]
    public async Task Handle_MissingSource_ThrowsNotFound()
    {
        var target = await AddBrandAsync("Apple");

        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            _sut.HandleAsync(Guid.NewGuid(), new MergeBrandRequest(target.Id), Ct));
    }

    [Fact]
    public async Task Handle_MissingTarget_ThrowsNotFound()
    {
        var source = await AddBrandAsync("Apple Inc");

        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            _sut.HandleAsync(source.Id, new MergeBrandRequest(Guid.NewGuid()), Ct));
    }
}
