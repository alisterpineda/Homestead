using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Finances;
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
        _sut = new PatchDurableHandler(_db, currentUser, _audit, new ItemLocationResolver(_db), new InventoryTagPathResolver(_db), _unitOfWork);

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
        ItemStatus status = ItemStatus.Active, Guid? areaId = null, string? statusNote = null,
        Guid? parentItemId = null, bool isContainer = false, string? compartment = null)
    {
        var item = new Item
        {
            Id = Guid.NewGuid(),
            Name = "Drill",
            Fungibility = Fungibility.NonFungible,
            Status = status,
            AreaId = areaId,
            StatusNote = statusNote,
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
        return _sut.HandleAsync(id, patch, Ct);
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
    public async Task Handle_SetBrand_MapsBrandIdAndName()
    {
        var apple = new Brand { Id = Guid.NewGuid(), Name = "Apple" };
        _db.Brands.Add(apple);
        await _db.SaveChangesAsync(Ct);
        var item = await AddItemAsync();

        var resp = await PatchAsync(item.Id, p => p.Replace(m => m.BrandId, apple.Id));

        Assert.Equal(apple.Id, resp.BrandId);
        Assert.Equal("Apple", resp.BrandName);
    }

    [Fact]
    public async Task Handle_SetUnknownBrand_Throws()
    {
        var item = await AddItemAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PatchAsync(item.Id, p => p.Replace(m => m.BrandId, Guid.NewGuid())));
    }

    [Fact]
    public async Task Handle_SetModel_TrimsAndStores()
    {
        var item = await AddItemAsync();

        var resp = await PatchAsync(item.Id, p => p.Replace(m => m.Model, "  MRW23  "));

        Assert.Equal("MRW23", resp.Model);
        Assert.Equal("MRW23", _db.Items.Single(i => i.Id == item.Id).Model);
    }

    [Fact]
    public async Task Handle_LentWithoutStatusNote_Throws()
    {
        var item = await AddItemAsync();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PatchAsync(item.Id, p => p.Replace(m => m.Status, ItemStatus.Lent)));

        Assert.Equal("StatusNote is required when the status is Lent.", ex.Message);
    }

    [Fact]
    public async Task Handle_LentWithBlankStatusNote_Throws()
    {
        var item = await AddItemAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PatchAsync(item.Id, p =>
            {
                p.Replace(m => m.Status, ItemStatus.Lent);
                p.Replace(m => m.StatusNote, "   ");
            }));
    }

    [Fact]
    public async Task Handle_LentWithStatusNote_SetsBoth()
    {
        var item = await AddItemAsync();

        var resp = await PatchAsync(item.Id, p =>
        {
            p.Replace(m => m.Status, ItemStatus.Lent);
            p.Replace(m => m.StatusNote, "  Dave next door  ");
        });

        Assert.Equal(ItemStatus.Lent, resp.Status);
        Assert.Equal("Dave next door", resp.StatusNote);
    }

    [Fact]
    public async Task Handle_LentToActive_ClearsStatusNote()
    {
        var item = await AddItemAsync(status: ItemStatus.Lent, statusNote: "Dave");

        var resp = await PatchAsync(item.Id, p => p.Replace(m => m.Status, ItemStatus.Active));

        Assert.Equal(ItemStatus.Active, resp.Status);
        Assert.Null(resp.StatusNote);
    }

    [Fact]
    public async Task Handle_LentToArchived_KeepsASuppliedNote()
    {
        // Archiving a lent item can record why, replacing the borrower note in the same patch.
        var item = await AddItemAsync(status: ItemStatus.Lent, statusNote: "Dave");

        var resp = await PatchAsync(item.Id, p =>
        {
            p.Replace(m => m.Status, ItemStatus.Archived);
            p.Replace(m => m.StatusNote, "  Sold to Sam  ");
        });

        Assert.Equal(ItemStatus.Archived, resp.Status);
        Assert.Equal("Sold to Sam", resp.StatusNote);
    }

    [Fact]
    public async Task Handle_ArchivedWithBlankNote_StoresNull()
    {
        var item = await AddItemAsync();

        var resp = await PatchAsync(item.Id, p =>
        {
            p.Replace(m => m.Status, ItemStatus.Archived);
            p.Replace(m => m.StatusNote, "   ");
        });

        Assert.Equal(ItemStatus.Archived, resp.Status);
        Assert.Null(resp.StatusNote);
        Assert.Null(_db.Items.Single(i => i.Id == item.Id).StatusNote);
    }

    [Fact]
    public async Task Handle_StatusNoteTooLong_Throws()
    {
        var item = await AddItemAsync();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PatchAsync(item.Id, p =>
            {
                p.Replace(m => m.Status, ItemStatus.Lent);
                p.Replace(m => m.StatusNote, new string('x', 257));
            }));

        Assert.Equal("StatusNote must be 256 characters or fewer.", ex.Message);
    }

    [Fact]
    public async Task Handle_ValueWithoutCurrency_Throws()
    {
        var item = await AddItemAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PatchAsync(item.Id, p => p.Replace(m => m.Value, 100m)));
    }

    private async Task<Commodity> AddCurrencyAsync(string symbol = "CAD")
    {
        var c = new Commodity { Id = Guid.NewGuid(), Symbol = symbol, Name = symbol, Kind = CommodityKind.Currency, DisplayPrecision = 2 };
        _db.Commodities.Add(c);
        await _db.SaveChangesAsync(Ct);
        return c;
    }

    [Fact]
    public async Task Handle_SetAcquisitionCluster_Persists()
    {
        var cad = await AddCurrencyAsync();
        var item = await AddItemAsync();

        var resp = await PatchAsync(item.Id, p =>
        {
            p.Replace(m => m.AcquisitionKind, AcquisitionKind.Purchased);
            p.Replace(m => m.AcquisitionCost, 189m);
            p.Replace(m => m.AcquisitionCostCurrencyId, cad.Id);
            p.Replace(m => m.AcquiredFrom, "  Home Depot  ");
        });

        Assert.Equal(AcquisitionKind.Purchased, resp.AcquisitionKind);
        Assert.Equal(189m, resp.AcquisitionCost);
        Assert.Equal(cad.Id, resp.AcquisitionCostCurrencyId);
        Assert.Equal("CAD", resp.AcquisitionCostCurrencySymbol);
        Assert.Equal("Home Depot", resp.AcquiredFrom);
    }

    [Fact]
    public async Task Handle_AcquisitionCostWithoutCurrency_Throws()
    {
        var item = await AddItemAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PatchAsync(item.Id, p => p.Replace(m => m.AcquisitionCost, 189m)));
    }

    [Fact]
    public async Task Handle_UndefinedAcquisitionKind_Throws()
    {
        var item = await AddItemAsync();

        // JSON Patch binds bare ints past the string converter; an out-of-range value must be a 400.
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PatchAsync(item.Id, p => p.Replace(m => m.AcquisitionKind, (AcquisitionKind)99)));
    }

    [Fact]
    public async Task Handle_UnrelatedPatch_PreservesAcquisitionFields()
    {
        var cad = await AddCurrencyAsync();
        var item = new Item
        {
            Id = Guid.NewGuid(),
            Name = "Drill",
            Fungibility = Fungibility.NonFungible,
            Status = ItemStatus.Active,
            IsContainer = false,
            AcquisitionKind = AcquisitionKind.Gift,
            AcquisitionCost = 50m,
            AcquisitionCostCurrencyId = cad.Id,
            AcquiredFrom = "Mom",
        };
        _db.Items.Add(item);
        await _db.SaveChangesAsync(Ct);

        // A rename must not silently null the acquisition cluster (guards the seed block).
        var resp = await PatchAsync(item.Id, p => p.Replace(m => m.Name, "Renamed"));

        Assert.Equal(AcquisitionKind.Gift, resp.AcquisitionKind);
        Assert.Equal(50m, resp.AcquisitionCost);
        Assert.Equal(cad.Id, resp.AcquisitionCostCurrencyId);
        Assert.Equal("Mom", resp.AcquiredFrom);
    }

    [Fact]
    public async Task Handle_TerminalStatus_KeepsLocationUntouched()
    {
        var item = await AddItemAsync(areaId: _garage.Id);

        // An "archive and move" in one patch: the move must be ignored for a terminal status.
        var resp = await PatchAsync(item.Id, p =>
        {
            p.Replace(m => m.Status, ItemStatus.Archived);
            p.Replace(m => m.AreaId, _office.Id);
        });

        Assert.Equal(ItemStatus.Archived, resp.Status);
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

    private async Task<Item> AddContainerAsync(
        Guid? areaId = null, Guid? parentItemId = null, bool isContainer = true)
    {
        var container = new Item
        {
            Id = Guid.NewGuid(),
            Name = "Tool chest",
            Fungibility = Fungibility.NonFungible,
            Status = ItemStatus.Active,
            IsContainer = isContainer,
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

        // Archive-and-move in one patch: the move/compartment ops are ignored for the transition.
        var resp = await PatchAsync(item.Id, p =>
        {
            p.Replace(m => m.Status, ItemStatus.Archived);
            p.Replace(m => m.AreaId, null);
            p.Replace(m => m.ParentItemId, chest.Id);
            p.Replace(m => m.Compartment, "Top shelf");
        });

        Assert.Equal(ItemStatus.Archived, resp.Status);
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
    public async Task Handle_CycleViaComponentEdge_Throws()
    {
        // A cycle can form through a component edge (non-container parents), not just container edges —
        // the cycle guard's link set spans every non-fungible item.
        var a = await AddContainerAsync(isContainer: false);
        var b = await AddContainerAsync(parentItemId: a.Id, isContainer: false);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PatchAsync(a.Id, p => p.Replace(m => m.ParentItemId, b.Id)));
    }

    [Fact]
    public async Task Handle_ParentFungible_Throws()
    {
        // A parent must be a non-fungible item; a fungible consumable cannot hold children.
        var consumable = new Item
        {
            Id = Guid.NewGuid(),
            Name = "Screws",
            Fungibility = Fungibility.Fungible,
            Status = ItemStatus.Active,
        };
        _db.Items.Add(consumable);
        await _db.SaveChangesAsync(Ct);
        var item = await AddItemAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PatchAsync(item.Id, p => p.Replace(m => m.ParentItemId, consumable.Id)));
    }

    [Fact]
    public async Task Handle_ParentMissing_Throws()
    {
        var item = await AddItemAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PatchAsync(item.Id, p => p.Replace(m => m.ParentItemId, Guid.NewGuid())));
    }

    [Fact]
    public async Task Handle_FlipWithTerminalChild_Throws()
    {
        // Flipping IsContainer is blocked while ANY child references the item, terminal records included.
        var chest = await AddContainerAsync(areaId: _garage.Id);
        await AddItemAsync(status: ItemStatus.Archived, parentItemId: chest.Id);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PatchAsync(chest.Id, p => p.Replace(m => m.IsContainer, false)));
    }

    [Fact]
    public async Task Handle_FlipWhenEmpty_Succeeds()
    {
        var chest = await AddContainerAsync(areaId: _garage.Id);

        var resp = await PatchAsync(chest.Id, p => p.Replace(m => m.IsContainer, false));

        Assert.Equal(false, resp.IsContainer);
    }

    [Fact]
    public async Task Handle_FlipToContainerWithComponent_Throws()
    {
        // The flip guard is symmetric: a non-container that holds a component can't become a container
        // either — that would reinterpret the component as stored contents.
        var pc = await AddContainerAsync(areaId: _garage.Id, isContainer: false);
        await AddItemAsync(status: ItemStatus.Active, parentItemId: pc.Id, compartment: "CPU");

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PatchAsync(pc.Id, p => p.Replace(m => m.IsContainer, true)));
    }

    [Fact]
    public async Task Handle_TerminalWithLiveChild_Throws()
    {
        var chest = await AddContainerAsync(areaId: _garage.Id);
        await AddItemAsync(status: ItemStatus.Active, parentItemId: chest.Id);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PatchAsync(chest.Id, p => p.Replace(m => m.Status, ItemStatus.Archived)));

        Assert.Equal("Cannot archive a container while it has live contents.", ex.Message);
    }

    [Fact]
    public async Task Handle_TerminalWithOnlyTerminalChildren_Succeeds_KindStaysContainer()
    {
        var chest = await AddContainerAsync(areaId: _garage.Id);
        await AddItemAsync(status: ItemStatus.Archived, parentItemId: chest.Id);

        var resp = await PatchAsync(chest.Id, p => p.Replace(m => m.Status, ItemStatus.Archived));

        Assert.Equal(ItemStatus.Archived, resp.Status);
        // Stays a container so the parent invariant holds for leftovers.
        Assert.Equal(true, resp.IsContainer);
    }

    [Fact]
    public async Task Handle_ArchiveAssemblyWithLiveComponent_Succeeds()
    {
        // A non-container's components ride along — archiving the assembly as a unit is never blocked
        // by them, unlike a container's live stored-in contents.
        var pc = await AddContainerAsync(areaId: _garage.Id, isContainer: false);
        await AddItemAsync(status: ItemStatus.Active, parentItemId: pc.Id, compartment: "CPU");

        var resp = await PatchAsync(pc.Id, p => p.Replace(m => m.Status, ItemStatus.Archived));

        Assert.Equal(ItemStatus.Archived, resp.Status);
        Assert.Equal(false, resp.IsContainer);
        // Its live component still makes it an assembly (derived from the surviving component child).
        Assert.True(resp.HasComponents);
    }

    [Fact]
    public async Task Handle_SealContainerWithLiveStoredChild_StaysBlocked()
    {
        // The counterpart to the assembly case: a container with a live stored-in child still blocks.
        var chest = await AddContainerAsync(areaId: _garage.Id, isContainer: true);
        await AddItemAsync(status: ItemStatus.Active, parentItemId: chest.Id);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PatchAsync(chest.Id, p => p.Replace(m => m.Status, ItemStatus.Archived)));
    }

    [Fact]
    public async Task Handle_ReplaceTags_SwapsEntireSet()
    {
        var ct = Ct;
        var item = await AddItemAsync();
        var oldTag = new Tag { Id = Guid.NewGuid(), Name = "old", Path = "old" };
        _db.InventoryTags.Add(oldTag);
        await _db.SaveChangesAsync(ct);
        _db.ItemTags.Add(new ItemTag { ItemId = item.Id, TagId = oldTag.Id });
        await _db.SaveChangesAsync(ct);

        var resp = await PatchAsync(item.Id, p => p.Replace(m => m.Tags, new List<string> { "tools/power" }));

        // The response reflects the new leaf tag only.
        Assert.Single(resp.Tags);
        Assert.Equal("tools/power", resp.Tags[0].Path);

        // Join rows swapped: old gone, new leaf present; ancestors upserted.
        var itemTagPaths = _db.ItemTags.Where(it => it.ItemId == item.Id)
            .Join(_db.InventoryTags, it => it.TagId, t => t.Id, (it, t) => t.Path).ToList();
        Assert.Equal(["tools/power"], itemTagPaths);
        Assert.Contains(_db.InventoryTags, t => t.Path == "tools");
    }

    [Fact]
    public async Task Handle_NoTagsOp_LeavesTagsUntouched()
    {
        var ct = Ct;
        var item = await AddItemAsync();
        var tag = new Tag { Id = Guid.NewGuid(), Name = "keep", Path = "keep" };
        _db.InventoryTags.Add(tag);
        await _db.SaveChangesAsync(ct);
        _db.ItemTags.Add(new ItemTag { ItemId = item.Id, TagId = tag.Id });
        await _db.SaveChangesAsync(ct);

        // A patch that never touches /tags must leave the item's tag set intact (seeded-from-current).
        var resp = await PatchAsync(item.Id, p => p.Replace(m => m.Name, "Renamed"));

        Assert.Single(resp.Tags);
        Assert.Equal("keep", resp.Tags[0].Path);
        Assert.Single(_db.ItemTags.Where(it => it.ItemId == item.Id));
    }

    [Fact]
    public async Task Handle_ClearTags_RemovesAllJoins()
    {
        var ct = Ct;
        var item = await AddItemAsync();
        var tag = new Tag { Id = Guid.NewGuid(), Name = "old", Path = "old" };
        _db.InventoryTags.Add(tag);
        await _db.SaveChangesAsync(ct);
        _db.ItemTags.Add(new ItemTag { ItemId = item.Id, TagId = tag.Id });
        await _db.SaveChangesAsync(ct);

        var resp = await PatchAsync(item.Id, p => p.Replace(m => m.Tags, new List<string>()));

        Assert.Empty(resp.Tags);
        Assert.Empty(_db.ItemTags.Where(it => it.ItemId == item.Id));
    }

    [Fact]
    public async Task Handle_MoveIntoNonContainer_SetsParentAndComponentLabel()
    {
        var pc = await AddContainerAsync(areaId: _garage.Id, isContainer: false);
        var cpu = await AddItemAsync(areaId: _office.Id);

        var resp = await PatchAsync(cpu.Id, p =>
        {
            p.Replace(m => m.AreaId, (Guid?)null);
            p.Replace(m => m.ParentItemId, pc.Id);
            p.Replace(m => m.Compartment, "CPU");
        });

        Assert.Equal(pc.Id, resp.ParentItemId);
        Assert.Equal(false, resp.ParentIsContainer);
        Assert.Equal("CPU", resp.Compartment);
        // A component resolves its area by walking up through its (non-container) parent.
        Assert.Equal(_garage.Id, resp.ResolvedAreaId);
    }
}
