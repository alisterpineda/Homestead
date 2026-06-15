using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Durables;
using Homestead.WebApi.Modules.Inventory.Features.Durables.PatchDurable;
using Homestead.WebApi.Modules.Inventory.Services;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Features.Durables;

public class PatchDurableHandlerTests : IAsyncLifetime
{
    private TestAppDbContext _db = null!;
    private IAuditContext _audit = null!;
    private IUnitOfWork _unitOfWork = null!;
    private PatchDurableHandler _sut = null!;
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
        _sut = new PatchDurableHandler(_db, currentUser, _audit, new ItemLocationResolver(_db), _unitOfWork);

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
        ItemStatus status = ItemStatus.Active, Guid? areaId = null, string? lentTo = null,
        Guid? parentItemId = null, bool isContainer = false, string? compartment = null)
    {
        var item = new Item
        {
            Id = Guid.NewGuid(),
            Name = "Drill",
            Fungibility = Fungibility.NonFungible,
            Status = status,
            AreaId = areaId,
            LentTo = lentTo,
            ParentItemId = parentItemId,
            IsContainer = isContainer,
            Compartment = compartment,
        };
        _db.Items.Add(item);
        await _db.SaveChangesAsync(Ct);
        return item;
    }

    private Task<DurableResponse> PatchAsync(Guid id, Action<JsonPatchDocument<PatchDurableModel>> build)
    {
        var patch = new JsonPatchDocument<PatchDurableModel>();
        build(patch);
        return _sut.Handle(new PatchDurableCommand { Id = id, PatchDocument = patch }, Ct);
    }

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

    private async Task<Item> AddContainerAsync(Guid? areaId = null, Guid? parentItemId = null)
    {
        var container = new Item
        {
            Id = Guid.NewGuid(),
            Name = "Tool chest",
            Fungibility = Fungibility.NonFungible,
            Status = ItemStatus.Active,
            IsContainer = true,
            AreaId = areaId,
            ParentItemId = parentItemId,
        };
        _db.Items.Add(container);
        await _db.SaveChangesAsync(Ct);
        return container;
    }

    [Fact]
    public async Task Handle_MoveIntoContainer_SetsParentResolvesPathAndCompartment()
    {
        var chest = await AddContainerAsync(areaId: _garage.Id);
        var item = await AddItemAsync(areaId: _office.Id);

        // A cross-kind move emits the op pair: clear /areaId, set /parentItemId.
        var resp = await PatchAsync(item.Id, p =>
        {
            p.Replace(m => m.AreaId, null);
            p.Replace(m => m.ParentItemId, chest.Id);
            p.Replace(m => m.Compartment, "Top shelf");
        });

        Assert.Equal(chest.Id, resp.ParentItemId);
        Assert.Null(resp.AreaId);
        Assert.Equal("Top shelf", resp.Compartment);
        Assert.Equal(_garage.Id, resp.ResolvedAreaId);
        Assert.Equal(2, resp.Path.Count);
        Assert.Equal(_garage.Id, resp.Path[0].Id);
        Assert.Equal(LocationSegmentKind.Area, resp.Path[0].Kind);
        Assert.Equal(chest.Id, resp.Path[1].Id);
        Assert.Equal(LocationSegmentKind.Container, resp.Path[1].Kind);
    }

    [Fact]
    public async Task Handle_ClearParent_AutoClearsCompartment()
    {
        var chest = await AddContainerAsync(areaId: _garage.Id);
        var item = await AddItemAsync(parentItemId: chest.Id, compartment: "Top shelf");

        var resp = await PatchAsync(item.Id, p => p.Replace(m => m.ParentItemId, null));

        Assert.Null(resp.ParentItemId);
        Assert.Null(resp.Compartment);
        Assert.Empty(resp.Path);
        Assert.Null(resp.ResolvedAreaId);
    }

    [Fact]
    public async Task Handle_AlreadyTerminal_MoveOutOfContainer_AppliesAndClearsCompartment()
    {
        // Escape hatch: an already-terminal record's location IS editable (the lock applies only
        // to the live→terminal transition).
        var chest = await AddContainerAsync(areaId: _garage.Id);
        var item = await AddItemAsync(status: ItemStatus.Archived, parentItemId: chest.Id, compartment: "Top shelf");

        var resp = await PatchAsync(item.Id, p => p.Replace(m => m.ParentItemId, null));

        Assert.Equal(ItemStatus.Archived, resp.Status);
        Assert.Null(resp.ParentItemId);
        Assert.Null(resp.Compartment);
    }

    [Fact]
    public async Task Handle_TransitionToTerminal_IgnoresParentAndCompartmentOps()
    {
        var chest = await AddContainerAsync(areaId: _garage.Id);
        var item = await AddItemAsync(areaId: _office.Id);

        // Sell-and-move in one patch: the move/compartment ops are ignored for the transition.
        var resp = await PatchAsync(item.Id, p =>
        {
            p.Replace(m => m.Status, ItemStatus.Sold);
            p.Replace(m => m.AreaId, null);
            p.Replace(m => m.ParentItemId, chest.Id);
            p.Replace(m => m.Compartment, "Top shelf");
        });

        Assert.Equal(ItemStatus.Sold, resp.Status);
        Assert.Equal(_office.Id, resp.AreaId);
        Assert.Null(resp.ParentItemId);
        Assert.Null(resp.Compartment);
    }

    [Fact]
    public async Task Handle_BothLocationsAfterPatch_Throws()
    {
        var chest = await AddContainerAsync(areaId: _garage.Id);
        var item = await AddItemAsync(areaId: _office.Id);

        // Setting a parent without clearing the area leaves both location FKs set.
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PatchAsync(item.Id, p => p.Replace(m => m.ParentItemId, chest.Id)));
    }

    [Fact]
    public async Task Handle_SelfParent_Throws()
    {
        var chest = await AddContainerAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PatchAsync(chest.Id, p => p.Replace(m => m.ParentItemId, chest.Id)));
    }

    [Fact]
    public async Task Handle_CycleViaDescendant_Throws()
    {
        var a = await AddContainerAsync();
        var b = await AddContainerAsync(parentItemId: a.Id);
        var c = await AddContainerAsync(parentItemId: b.Id);

        // Placing A inside its own grandchild C would form a cycle.
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PatchAsync(a.Id, p => p.Replace(m => m.ParentItemId, c.Id)));
    }

    [Fact]
    public async Task Handle_ParentNotContainer_Throws()
    {
        var plain = await AddItemAsync();
        var item = await AddItemAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PatchAsync(item.Id, p => p.Replace(m => m.ParentItemId, plain.Id)));
    }

    [Fact]
    public async Task Handle_ParentMissing_Throws()
    {
        var item = await AddItemAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PatchAsync(item.Id, p => p.Replace(m => m.ParentItemId, Guid.NewGuid())));
    }

    [Fact]
    public async Task Handle_RoleOffWithTerminalChild_Throws()
    {
        // Rule B: role-off is blocked while ANY child references the item, terminal records included.
        var chest = await AddContainerAsync(areaId: _garage.Id);
        await AddItemAsync(status: ItemStatus.Archived, parentItemId: chest.Id);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PatchAsync(chest.Id, p => p.Replace(m => m.IsContainer, false)));
    }

    [Fact]
    public async Task Handle_RoleOffWhenEmpty_Succeeds()
    {
        var chest = await AddContainerAsync(areaId: _garage.Id);

        var resp = await PatchAsync(chest.Id, p => p.Replace(m => m.IsContainer, false));

        Assert.False(resp.IsContainer);
    }

    [Fact]
    public async Task Handle_TerminalWithLiveChild_Throws()
    {
        var chest = await AddContainerAsync(areaId: _garage.Id);
        await AddItemAsync(status: ItemStatus.Active, parentItemId: chest.Id);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PatchAsync(chest.Id, p => p.Replace(m => m.Status, ItemStatus.Sold)));
    }

    [Fact]
    public async Task Handle_TerminalWithOnlyTerminalChildren_Succeeds_FlagStaysOn()
    {
        var chest = await AddContainerAsync(areaId: _garage.Id);
        await AddItemAsync(status: ItemStatus.Sold, parentItemId: chest.Id);

        var resp = await PatchAsync(chest.Id, p => p.Replace(m => m.Status, ItemStatus.Sold));

        Assert.Equal(ItemStatus.Sold, resp.Status);
        // The container role stays on so the parent-is-container invariant holds for leftovers.
        Assert.True(resp.IsContainer);
    }
}
