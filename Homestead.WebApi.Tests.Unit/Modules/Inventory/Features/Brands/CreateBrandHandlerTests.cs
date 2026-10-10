using FluentValidation;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Brands.CreateBrand;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Features.Brands;

public class CreateBrandHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IAuditContext _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly CreateBrandHandler _sut;

    public CreateBrandHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _audit = Substitute.For<IAuditContext>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));
        _sut = new CreateBrandHandler(_db, _audit, _unitOfWork, new CreateBrandCommandValidator());
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
    public async Task Handle_NewName_CreatesBrand()
    {
        var resp = await _sut.Handle(new CreateBrandCommand { Name = "Apple" }, Ct);

        Assert.Equal("Apple", resp.Name);
        Assert.Contains(_db.Brands, b => b.Id == resp.Id && b.Name == "Apple");
    }

    [Fact]
    public async Task Handle_NewName_LogsCreatedAudit()
    {
        var resp = await _sut.Handle(new CreateBrandCommand { Name = "Apple" }, Ct);

        _audit.Received(1).Created("Brand", resp.Id, Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_TrimsName()
    {
        var resp = await _sut.Handle(new CreateBrandCommand { Name = "  Apple  " }, Ct);

        Assert.Equal("Apple", resp.Name);
        Assert.Equal("Apple", _db.Brands.Single().Name);
    }

    [Fact]
    public async Task Handle_ExistingName_ReturnsExistingRow_NoDuplicate()
    {
        var existing = await AddBrandAsync("Apple");

        var resp = await _sut.Handle(new CreateBrandCommand { Name = "Apple" }, Ct);

        Assert.Equal(existing.Id, resp.Id);
        Assert.Single(_db.Brands);
    }

    [Fact]
    public async Task Handle_CaseAndWhitespaceVariant_ReturnsExistingRow_NoDuplicate()
    {
        // Find-or-create dedups a trimmed/case-variant name (canonicalisation).
        var existing = await AddBrandAsync("Apple");

        var resp = await _sut.Handle(new CreateBrandCommand { Name = "  apple " }, Ct);

        Assert.Equal(existing.Id, resp.Id);
        Assert.Single(_db.Brands);
    }

    [Fact]
    public async Task Handle_ExistingName_DoesNotLogCreatedAudit()
    {
        await AddBrandAsync("Apple");

        await _sut.Handle(new CreateBrandCommand { Name = "apple" }, Ct);

        _audit.DidNotReceive().Created("Brand", Arg.Any<Guid>(), Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_BlankName_ThrowsValidationException()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.Handle(new CreateBrandCommand { Name = "   " }, Ct));
    }
}
