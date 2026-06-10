using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Areas.DeleteArea;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Features.Areas;

public class DeleteAreaHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IAuditContext _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly DeleteAreaHandler _sut;

    public DeleteAreaHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _audit = Substitute.For<IAuditContext>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));
        _sut = new DeleteAreaHandler(_db, _audit, _unitOfWork);
    }

    public void Dispose() => _db.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Area> AddAreaWithItemAsync(ItemStatus? itemStatus)
    {
        var area = new Area { Id = Guid.NewGuid(), Name = "Garage" };
        _db.Areas.Add(area);
        if (itemStatus is { } status)
        {
            _db.Items.Add(new Item
            {
                Id = Guid.NewGuid(),
                Name = "Drill",
                Fungibility = Fungibility.NonFungible,
                Status = status,
                AreaId = area.Id,
            });
        }
        await _db.SaveChangesAsync(Ct);
        return area;
    }

    [Fact]
    public async Task Handle_EmptyArea_Deletes()
    {
        var area = await AddAreaWithItemAsync(itemStatus: null);

        var result = await _sut.Handle(new DeleteAreaCommand(area.Id), Ct);

        Assert.True(result);
        Assert.Empty(_db.Areas);
        _audit.Received(1).Deleted("Area", area.Id, "Garage", Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_WithLiveItem_Throws()
    {
        var area = await AddAreaWithItemAsync(ItemStatus.Active);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.Handle(new DeleteAreaCommand(area.Id), Ct));

        Assert.Contains(_db.Areas, a => a.Id == area.Id);
    }

    [Fact]
    public async Task Handle_WithOnlyTerminalItem_Deletes()
    {
        var area = await AddAreaWithItemAsync(ItemStatus.Sold);

        var result = await _sut.Handle(new DeleteAreaCommand(area.Id), Ct);

        Assert.True(result);
    }

    [Fact]
    public async Task Handle_MissingArea_ReturnsFalse()
    {
        var result = await _sut.Handle(new DeleteAreaCommand(Guid.NewGuid()), Ct);

        Assert.False(result);
    }
}
