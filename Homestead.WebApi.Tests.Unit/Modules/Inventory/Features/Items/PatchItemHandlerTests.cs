using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Items.PatchItem;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Features.Items;

public class PatchItemHandlerTests : IAsyncLifetime
{
    private TestAppDbContext _db = null!;
    private IAuditContext _audit = null!;
    private IUnitOfWork _unitOfWork = null!;
    private PatchItemHandler _sut = null!;
    private Area _garage = null!;
    private Area _office = null!;

    public async ValueTask InitializeAsync()
    {
        _db = TestAppDbContext.Create();
        _audit = Substitute.For<IAuditContext>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(Guid.NewGuid());
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));
        _sut = new PatchItemHandler(_db, currentUser, _audit, _unitOfWork);

        _garage = new Area { Id = Guid.NewGuid(), Name = "Garage" };
        _office = new Area { Id = Guid.NewGuid(), Name = "Office" };
        _db.Areas.AddRange(_garage, _office);
        await _db.SaveChangesAsync();
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Item> AddItemAsync(
        ItemStatus status = ItemStatus.Active, Guid? areaId = null, string? lentTo = null)
    {
        var item = new Item
        {
            Id = Guid.NewGuid(),
            Name = "Drill",
            Fungibility = Fungibility.NonFungible,
            Status = status,
            AreaId = areaId,
            LentTo = lentTo,
        };
        _db.Items.Add(item);
        await _db.SaveChangesAsync(Ct);
        return item;
    }

    private async Task<ItemResponseLike> PatchAsync(Guid id, Action<JsonPatchDocument<PatchItemModel>> build)
    {
        var patch = new JsonPatchDocument<PatchItemModel>();
        build(patch);
        var resp = await _sut.Handle(new PatchItemCommand { Id = id, PatchDocument = patch }, Ct);
        return new ItemResponseLike(resp.AreaId, resp.AreaName, resp.Status, resp.LentTo);
    }

    private record ItemResponseLike(Guid? AreaId, string? AreaName, ItemStatus Status, string? LentTo);

    [Fact]
    public async Task Handle_Move_ChangesArea()
    {
        var item = await AddItemAsync(areaId: _garage.Id);

        var resp = await PatchAsync(item.Id, p => p.Replace(m => m.AreaId, _office.Id));

        Assert.Equal(_office.Id, resp.AreaId);
        Assert.Equal("Office", resp.AreaName);
    }

    [Fact]
    public async Task Handle_Unplace_ClearsArea()
    {
        var item = await AddItemAsync(areaId: _garage.Id);

        var resp = await PatchAsync(item.Id, p => p.Replace(m => m.AreaId, null));

        Assert.Null(resp.AreaId);
        Assert.Null(resp.AreaName);
    }

    [Fact]
    public async Task Handle_MoveToUnknownArea_Throws()
    {
        var item = await AddItemAsync(areaId: _garage.Id);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PatchAsync(item.Id, p => p.Replace(m => m.AreaId, Guid.NewGuid())));
    }

    [Fact]
    public async Task Handle_LentWithoutLentTo_Throws()
    {
        var item = await AddItemAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PatchAsync(item.Id, p => p.Replace(m => m.Status, ItemStatus.Lent)));
    }

    [Fact]
    public async Task Handle_LentWithLentTo_SetsBoth()
    {
        var item = await AddItemAsync();

        var resp = await PatchAsync(item.Id, p =>
        {
            p.Replace(m => m.Status, ItemStatus.Lent);
            p.Replace(m => m.LentTo, "Dave next door");
        });

        Assert.Equal(ItemStatus.Lent, resp.Status);
        Assert.Equal("Dave next door", resp.LentTo);
    }

    [Fact]
    public async Task Handle_LeavingLent_ClearsLentTo()
    {
        var item = await AddItemAsync(status: ItemStatus.Lent, lentTo: "Dave");

        var resp = await PatchAsync(item.Id, p => p.Replace(m => m.Status, ItemStatus.Active));

        Assert.Equal(ItemStatus.Active, resp.Status);
        Assert.Null(resp.LentTo);
    }

    [Fact]
    public async Task Handle_ValueWithoutCurrency_Throws()
    {
        var item = await AddItemAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PatchAsync(item.Id, p => p.Replace(m => m.Value, 100m)));
    }

    [Fact]
    public async Task Handle_TerminalStatus_KeepsLocationUntouched()
    {
        var item = await AddItemAsync(areaId: _garage.Id);

        // A "sell and move" in one patch: the move must be ignored for a terminal status.
        var resp = await PatchAsync(item.Id, p =>
        {
            p.Replace(m => m.Status, ItemStatus.Sold);
            p.Replace(m => m.AreaId, _office.Id);
        });

        Assert.Equal(ItemStatus.Sold, resp.Status);
        Assert.Equal(_garage.Id, resp.AreaId);
    }

    [Fact]
    public async Task Handle_TracksAudit()
    {
        var item = await AddItemAsync();

        await PatchAsync(item.Id, p => p.Replace(m => m.Name, "Renamed"));

        _audit.Received(1).Track<ItemSnapshot, Item>(Arg.Any<Item>(), "Item", item.Id, Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_MissingItem_Throws()
    {
        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            PatchAsync(Guid.NewGuid(), p => p.Replace(m => m.Name, "X")));
    }
}
