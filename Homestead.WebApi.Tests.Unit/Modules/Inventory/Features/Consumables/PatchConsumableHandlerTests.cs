using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Consumables;
using Homestead.WebApi.Modules.Inventory.Features.Consumables.PatchConsumable;
using Homestead.WebApi.Modules.Inventory.Services;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Features.Consumables;

public class PatchConsumableHandlerTests : IAsyncLifetime
{
    private TestAppDbContext _db = null!;
    private PatchConsumableHandler _sut = null!;

    public async ValueTask InitializeAsync()
    {
        _db = TestAppDbContext.Create();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(Guid.NewGuid());
        var audit = Substitute.For<IAuditContext>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));
        _sut = new PatchConsumableHandler(_db, currentUser, audit, new InventoryTagPathResolver(_db), unitOfWork);
        await ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Item> AddConsumableAsync()
    {
        var item = new Item
        {
            Id = Guid.NewGuid(),
            Name = "Wood screws",
            Fungibility = Fungibility.Fungible,
            Status = ItemStatus.Active,
            Unit = "pcs",
        };
        _db.Items.Add(item);
        await _db.SaveChangesAsync(Ct);
        return item;
    }

    private Task<ConsumableResponse> PatchAsync(Guid id, Action<JsonPatchDocument<PatchConsumableModel>> build)
    {
        var patch = new JsonPatchDocument<PatchConsumableModel>();
        build(patch);
        return _sut.HandleAsync(id, patch, Ct);
    }

    [Fact]
    public async Task Handle_SetBrand_MapsBrandIdAndName()
    {
        var apple = new Brand { Id = Guid.NewGuid(), Name = "Apple" };
        _db.Brands.Add(apple);
        await _db.SaveChangesAsync(Ct);
        var item = await AddConsumableAsync();

        var resp = await PatchAsync(item.Id, p => p.Replace(m => m.BrandId, apple.Id));

        Assert.Equal(apple.Id, resp.BrandId);
        Assert.Equal("Apple", resp.BrandName);
    }

    [Fact]
    public async Task Handle_SetUnknownBrand_Throws()
    {
        var item = await AddConsumableAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PatchAsync(item.Id, p => p.Replace(m => m.BrandId, Guid.NewGuid())));
    }

    [Fact]
    public async Task Handle_ArchiveWithNote_PersistsBoth()
    {
        var item = await AddConsumableAsync();

        var resp = await PatchAsync(item.Id, p =>
        {
            p.Replace(m => m.Status, ItemStatus.Archived);
            p.Replace(m => m.StatusNote, "  Used up  ");
        });

        Assert.Equal(ItemStatus.Archived, resp.Status);
        Assert.Equal("Used up", resp.StatusNote);
        Assert.Equal("Used up", _db.Items.Single(i => i.Id == item.Id).StatusNote);
    }

    [Fact]
    public async Task Handle_ArchiveWithBlankNote_StoresNull()
    {
        var item = await AddConsumableAsync();

        var resp = await PatchAsync(item.Id, p =>
        {
            p.Replace(m => m.Status, ItemStatus.Archived);
            p.Replace(m => m.StatusNote, "   ");
        });

        Assert.Equal(ItemStatus.Archived, resp.Status);
        Assert.Null(resp.StatusNote);
    }

    [Fact]
    public async Task Handle_Restore_ClearsStatusNote()
    {
        var item = await AddConsumableAsync();
        item.Status = ItemStatus.Archived;
        item.StatusNote = "Used up";
        await _db.SaveChangesAsync(Ct);

        var resp = await PatchAsync(item.Id, p => p.Replace(m => m.Status, ItemStatus.Active));

        Assert.Equal(ItemStatus.Active, resp.Status);
        Assert.Null(resp.StatusNote);
        Assert.Null(_db.Items.Single(i => i.Id == item.Id).StatusNote);
    }

    [Fact]
    public async Task Handle_StatusNoteTooLong_Throws()
    {
        var item = await AddConsumableAsync();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PatchAsync(item.Id, p =>
            {
                p.Replace(m => m.Status, ItemStatus.Archived);
                p.Replace(m => m.StatusNote, new string('x', 257));
            }));

        Assert.Equal("StatusNote must be 256 characters or fewer.", ex.Message);
    }

    [Fact]
    public async Task Handle_ReplaceTags_SwapsEntireSet()
    {
        var ct = Ct;
        var item = await AddConsumableAsync();
        var oldTag = new Tag { Id = Guid.NewGuid(), Name = "old", Path = "old" };
        _db.InventoryTags.Add(oldTag);
        await _db.SaveChangesAsync(ct);
        _db.ItemTags.Add(new ItemTag { ItemId = item.Id, TagId = oldTag.Id });
        await _db.SaveChangesAsync(ct);

        var resp = await PatchAsync(item.Id, p => p.Replace(m => m.Tags, new List<string> { "hardware/fasteners" }));

        Assert.Single(resp.Tags);
        Assert.Equal("hardware/fasteners", resp.Tags[0].Path);

        var itemTagPaths = _db.ItemTags.Where(it => it.ItemId == item.Id)
            .Join(_db.InventoryTags, it => it.TagId, t => t.Id, (it, t) => t.Path).ToList();
        Assert.Equal(["hardware/fasteners"], itemTagPaths);
        Assert.Contains(_db.InventoryTags, t => t.Path == "hardware");
    }

    [Fact]
    public async Task Handle_NoTagsOp_LeavesTagsUntouched()
    {
        var ct = Ct;
        var item = await AddConsumableAsync();
        var tag = new Tag { Id = Guid.NewGuid(), Name = "keep", Path = "keep" };
        _db.InventoryTags.Add(tag);
        await _db.SaveChangesAsync(ct);
        _db.ItemTags.Add(new ItemTag { ItemId = item.Id, TagId = tag.Id });
        await _db.SaveChangesAsync(ct);

        var resp = await PatchAsync(item.Id, p => p.Replace(m => m.Name, "Renamed"));

        Assert.Single(resp.Tags);
        Assert.Equal("keep", resp.Tags[0].Path);
        Assert.Single(_db.ItemTags.Where(it => it.ItemId == item.Id));
    }
}
