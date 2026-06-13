using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Areas.CreateArea;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Features.Areas;

public class CreateAreaHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IAuditContext _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly CreateAreaHandler _sut;

    public CreateAreaHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _audit = Substitute.For<IAuditContext>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));
        _sut = new CreateAreaHandler(_db, _audit, _unitOfWork);
    }

    public void Dispose() => _db.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Area> AddAreaAsync(string name, Guid? parentAreaId = null)
    {
        var area = new Area { Id = Guid.NewGuid(), Name = name, ParentAreaId = parentAreaId };
        _db.Areas.Add(area);
        await _db.SaveChangesAsync(Ct);
        return area;
    }

    [Fact]
    public async Task Handle_TopLevel_CreatesRootArea()
    {
        var resp = await _sut.Handle(new CreateAreaCommand { Name = "House" }, Ct);

        Assert.Null(resp.ParentAreaId);
        Assert.Null(resp.ParentAreaName);
        Assert.Contains(_db.Areas, a => a.Id == resp.Id && a.ParentAreaId == null);
    }

    [Fact]
    public async Task Handle_WithParent_NestsAndResolvesParentName()
    {
        var house = await AddAreaAsync("House");

        var resp = await _sut.Handle(new CreateAreaCommand { Name = "Basement", ParentAreaId = house.Id }, Ct);

        Assert.Equal(house.Id, resp.ParentAreaId);
        Assert.Equal("House", resp.ParentAreaName);
    }

    [Fact]
    public async Task Handle_ParentNotFound_Throws()
    {
        // BusinessRuleException (400), matching PatchArea and the item handlers' bad-reference convention.
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.Handle(new CreateAreaCommand { Name = "Orphan", ParentAreaId = Guid.NewGuid() }, Ct));

        Assert.Empty(_db.Areas);
    }
}
