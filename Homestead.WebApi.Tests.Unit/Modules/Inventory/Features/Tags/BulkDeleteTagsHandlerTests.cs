using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Tags.BulkDeleteTags;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Features.Tags;

public class BulkDeleteTagsHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IAuditContext _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly BulkDeleteTagsHandler _sut;

    public BulkDeleteTagsHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _audit = Substitute.For<IAuditContext>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));
        _sut = new BulkDeleteTagsHandler(_db, _audit, _unitOfWork);
    }

    public void Dispose() => _db.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Handle_LogsDeletedAuditPerTag()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        _db.InventoryTags.AddRange(
            new Tag { Id = id1, Name = "Tag A", Path = "Tag A" },
            new Tag { Id = id2, Name = "Tag B", Path = "Tag B" });
        await _db.SaveChangesAsync(Ct);

        var result = await _sut.Handle(new BulkDeleteInventoryTagsCommand([id1, id2]), Ct);

        Assert.Equal(2, result.DeletedCount);
        _audit.Received(1).Deleted("InventoryTag", id1, "Tag A", Arg.Any<Guid?>());
        _audit.Received(1).Deleted("InventoryTag", id2, "Tag B", Arg.Any<Guid?>());
        Assert.Empty(_db.InventoryTags);
    }

    [Fact]
    public async Task Handle_NoTags_DoesNotLogAudit_ReturnsZero()
    {
        var result = await _sut.Handle(new BulkDeleteInventoryTagsCommand([Guid.NewGuid()]), Ct);

        Assert.Equal(0, result.DeletedCount);
        _audit.DidNotReceive().Deleted(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_EmptyIds_ReturnsZero()
    {
        var result = await _sut.Handle(new BulkDeleteInventoryTagsCommand([]), Ct);

        Assert.Equal(0, result.DeletedCount);
    }

    [Fact]
    public async Task Handle_DeleteMiddleTag_ReparentsChildToGrandparent_AndRewritesPath()
    {
        var ct = Ct;
        var a = new Tag { Id = Guid.NewGuid(), Name = "a", Path = "a" };
        _db.InventoryTags.Add(a);
        await _db.SaveChangesAsync(ct);
        var b = new Tag { Id = Guid.NewGuid(), Name = "b", Path = "a/b", ParentId = a.Id };
        _db.InventoryTags.Add(b);
        await _db.SaveChangesAsync(ct);
        var c = new Tag { Id = Guid.NewGuid(), Name = "c", Path = "a/b/c", ParentId = b.Id };
        _db.InventoryTags.Add(c);
        await _db.SaveChangesAsync(ct);

        var result = await _sut.Handle(new BulkDeleteInventoryTagsCommand([b.Id]), ct);

        Assert.Equal(1, result.DeletedCount);
        Assert.False(await _db.InventoryTags.AnyAsync(t => t.Id == b.Id, ct));
        var reloadedC = await _db.InventoryTags.SingleAsync(t => t.Id == c.Id, ct);
        Assert.Equal(a.Id, reloadedC.ParentId);
        Assert.Equal("a/c", reloadedC.Path);
    }

    [Fact]
    public async Task Handle_DeleteParentAndMiddle_DeepestFirst_PromotesGrandchildToRoot()
    {
        var ct = Ct;
        var a = new Tag { Id = Guid.NewGuid(), Name = "a", Path = "a" };
        _db.InventoryTags.Add(a);
        await _db.SaveChangesAsync(ct);
        var b = new Tag { Id = Guid.NewGuid(), Name = "b", Path = "a/b", ParentId = a.Id };
        _db.InventoryTags.Add(b);
        await _db.SaveChangesAsync(ct);
        var c = new Tag { Id = Guid.NewGuid(), Name = "c", Path = "a/b/c", ParentId = b.Id };
        _db.InventoryTags.Add(c);
        await _db.SaveChangesAsync(ct);

        // Delete both the root and the middle: c must cascade-reparent all the way up to root.
        var result = await _sut.Handle(new BulkDeleteInventoryTagsCommand([a.Id, b.Id]), ct);

        Assert.Equal(2, result.DeletedCount);
        var reloadedC = await _db.InventoryTags.SingleAsync(t => t.Id == c.Id, ct);
        Assert.Null(reloadedC.ParentId);
        Assert.Equal("c", reloadedC.Path);
    }

    [Fact]
    public async Task Handle_DeleteColouredMiddle_RecomputesReparentedDescendantColour()
    {
        var ct = Ct;
        var a = new Tag { Id = Guid.NewGuid(), Name = "a", Path = "a", Color = "#aaaaaa", EffectiveColor = "#aaaaaa" };
        _db.InventoryTags.Add(a);
        await _db.SaveChangesAsync(ct);
        var b = new Tag { Id = Guid.NewGuid(), Name = "b", Path = "a/b", ParentId = a.Id, Color = "#bbbbbb", EffectiveColor = "#bbbbbb" };
        _db.InventoryTags.Add(b);
        await _db.SaveChangesAsync(ct);
        // c inherits b's colour today; after b is deleted it re-parents to a and must inherit a's colour.
        var c = new Tag { Id = Guid.NewGuid(), Name = "c", Path = "a/b/c", ParentId = b.Id, EffectiveColor = "#bbbbbb" };
        _db.InventoryTags.Add(c);
        await _db.SaveChangesAsync(ct);

        await _sut.Handle(new BulkDeleteInventoryTagsCommand([b.Id]), ct);

        var reloadedC = await _db.InventoryTags.SingleAsync(t => t.Id == c.Id, ct);
        Assert.Null(reloadedC.Color);
        Assert.Equal("#aaaaaa", reloadedC.EffectiveColor);
    }

    [Fact]
    public async Task Handle_RemovesItemTagJoins_ButKeepsItems()
    {
        var ct = Ct;
        var tag = new Tag { Id = Guid.NewGuid(), Name = "tools", Path = "tools" };
        _db.InventoryTags.Add(tag);
        var item = new Item
        {
            Id = Guid.NewGuid(),
            Name = "Drill",
            Fungibility = Fungibility.NonFungible,
            Status = ItemStatus.Active,
            IsContainer = false,
        };
        _db.Items.Add(item);
        await _db.SaveChangesAsync(ct);
        _db.ItemTags.Add(new ItemTag { ItemId = item.Id, TagId = tag.Id });
        await _db.SaveChangesAsync(ct);

        var result = await _sut.Handle(new BulkDeleteInventoryTagsCommand([tag.Id]), ct);

        Assert.Equal(1, result.DeletedCount);
        Assert.Empty(_db.ItemTags);
        Assert.True(await _db.Items.AnyAsync(i => i.Id == item.Id, ct));
    }
}
