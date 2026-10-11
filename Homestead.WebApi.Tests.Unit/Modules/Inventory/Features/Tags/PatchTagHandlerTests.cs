using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Tags.PatchTag;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Features.Tags;

public class PatchTagHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IAuditContext _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly PatchTagHandler _sut;

    public PatchTagHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _audit = Substitute.For<IAuditContext>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(Guid.NewGuid());
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));
        _sut = new PatchTagHandler(_db, _audit, currentUser, _unitOfWork);
    }

    public void Dispose() => _db.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<PatchInventoryTagResponse> PatchAsync(Guid id, Action<JsonPatchDocument<PatchInventoryTagModel>> build)
    {
        var patch = new JsonPatchDocument<PatchInventoryTagModel>();
        build(patch);
        return _sut.HandleAsync(id, patch, Ct);
    }

    [Fact]
    public async Task Handle_MissingTag_Throws()
    {
        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            PatchAsync(Guid.NewGuid(), p => p.Replace(m => m.Name, "New")));
    }

    [Fact]
    public async Task Handle_WithNameChange_TracksAudit()
    {
        var tag = new Tag { Id = Guid.NewGuid(), Name = "Old", Path = "Old" };
        _db.InventoryTags.Add(tag);
        await _db.SaveChangesAsync(Ct);

        await PatchAsync(tag.Id, p => p.Replace(m => m.Name, "New"));

        _audit.Received(1).Track<InventoryTagSnapshot, Tag>(Arg.Any<Tag>(), "InventoryTag", tag.Id, Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_NoChanges_StillCallsTrack()
    {
        var tag = new Tag { Id = Guid.NewGuid(), Name = "Same", Path = "Same" };
        _db.InventoryTags.Add(tag);
        await _db.SaveChangesAsync(Ct);

        await PatchAsync(tag.Id, p => p.Replace(m => m.Name, "Same"));

        _audit.Received(1).Track<InventoryTagSnapshot, Tag>(Arg.Any<Tag>(), "InventoryTag", tag.Id, Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_EmptyName_Throws()
    {
        var tag = new Tag { Id = Guid.NewGuid(), Name = "Old", Path = "Old" };
        _db.InventoryTags.Add(tag);
        await _db.SaveChangesAsync(Ct);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PatchAsync(tag.Id, p => p.Replace(m => m.Name, "   ")));
    }

    [Fact]
    public async Task Handle_NameWithSlash_Throws()
    {
        var tag = new Tag { Id = Guid.NewGuid(), Name = "Old", Path = "Old" };
        _db.InventoryTags.Add(tag);
        await _db.SaveChangesAsync(Ct);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PatchAsync(tag.Id, p => p.Replace(m => m.Name, "a/b")));
    }

    [Fact]
    public async Task Handle_SelfParent_Throws()
    {
        var tag = new Tag { Id = Guid.NewGuid(), Name = "Old", Path = "Old" };
        _db.InventoryTags.Add(tag);
        await _db.SaveChangesAsync(Ct);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PatchAsync(tag.Id, p => p.Replace(m => m.ParentId, tag.Id)));
    }

    [Fact]
    public async Task Handle_UnknownParent_Throws()
    {
        var tag = new Tag { Id = Guid.NewGuid(), Name = "Old", Path = "Old" };
        _db.InventoryTags.Add(tag);
        await _db.SaveChangesAsync(Ct);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PatchAsync(tag.Id, p => p.Replace(m => m.ParentId, Guid.NewGuid())));
    }

    [Fact]
    public async Task Handle_MoveUnderOwnDescendant_Throws()
    {
        var a = new Tag { Id = Guid.NewGuid(), Name = "a", Path = "a" };
        _db.InventoryTags.Add(a);
        await _db.SaveChangesAsync(Ct);
        var b = new Tag { Id = Guid.NewGuid(), Name = "b", Path = "a/b", ParentId = a.Id };
        _db.InventoryTags.Add(b);
        await _db.SaveChangesAsync(Ct);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PatchAsync(a.Id, p => p.Replace(m => m.ParentId, b.Id)));
    }

    [Fact]
    public async Task Handle_NameChange_ReDerivesPathAndDescendantPaths()
    {
        var ct = Ct;
        var parent = new Tag { Id = Guid.NewGuid(), Name = "old", Path = "old" };
        _db.InventoryTags.Add(parent);
        await _db.SaveChangesAsync(ct);
        var child = new Tag { Id = Guid.NewGuid(), Name = "child", Path = "old/child", ParentId = parent.Id };
        var grandchild = new Tag { Id = Guid.NewGuid(), Name = "leaf", Path = "old/child/leaf", ParentId = child.Id };
        _db.InventoryTags.AddRange(child, grandchild);
        await _db.SaveChangesAsync(ct);

        var resp = await PatchAsync(parent.Id, p => p.Replace(m => m.Name, "new"));

        Assert.Equal("new", resp.Path);
        var reloadedChild = await _db.InventoryTags.SingleAsync(t => t.Id == child.Id, ct);
        var reloadedGrandchild = await _db.InventoryTags.SingleAsync(t => t.Id == grandchild.Id, ct);
        Assert.Equal("new/child", reloadedChild.Path);
        Assert.Equal("new/child/leaf", reloadedGrandchild.Path);
    }

    [Fact]
    public async Task Handle_ParentChange_ReDerivesPath()
    {
        var ct = Ct;
        var leaf = new Tag { Id = Guid.NewGuid(), Name = "leaf", Path = "leaf" };
        var target = new Tag { Id = Guid.NewGuid(), Name = "target", Path = "target" };
        _db.InventoryTags.AddRange(leaf, target);
        await _db.SaveChangesAsync(ct);

        var resp = await PatchAsync(leaf.Id, p => p.Replace(m => m.ParentId, target.Id));

        Assert.Equal(target.Id, resp.ParentId);
        Assert.Equal("target/leaf", resp.Path);
    }

    [Fact]
    public async Task Handle_RenameToExistingPath_ThrowsDuplicate()
    {
        var ct = Ct;
        var existing = new Tag { Id = Guid.NewGuid(), Name = "existing", Path = "existing" };
        var other = new Tag { Id = Guid.NewGuid(), Name = "other", Path = "other" };
        _db.InventoryTags.AddRange(existing, other);
        await _db.SaveChangesAsync(ct);

        await Assert.ThrowsAsync<DuplicateEntityException>(() =>
            PatchAsync(other.Id, p => p.Replace(m => m.Name, "existing")));
    }

    [Fact]
    public async Task Handle_SetColor_CascadesEffectiveColorToInheritingDescendants()
    {
        var ct = Ct;
        var root = new Tag { Id = Guid.NewGuid(), Name = "root", Path = "root" };
        _db.InventoryTags.Add(root);
        await _db.SaveChangesAsync(ct);
        var child = new Tag { Id = Guid.NewGuid(), Name = "child", Path = "root/child", ParentId = root.Id };
        var grandchild = new Tag { Id = Guid.NewGuid(), Name = "leaf", Path = "root/child/leaf", ParentId = child.Id };
        _db.InventoryTags.AddRange(child, grandchild);
        await _db.SaveChangesAsync(ct);

        var resp = await PatchAsync(root.Id, p => p.Replace(m => m.Color, "#ff0000"));

        Assert.Equal("#ff0000", resp.Color);
        Assert.Equal("#ff0000", resp.EffectiveColor);
        var reloadedChild = await _db.InventoryTags.SingleAsync(t => t.Id == child.Id, ct);
        var reloadedGrandchild = await _db.InventoryTags.SingleAsync(t => t.Id == grandchild.Id, ct);
        Assert.Null(reloadedChild.Color);
        Assert.Equal("#ff0000", reloadedChild.EffectiveColor);
        Assert.Equal("#ff0000", reloadedGrandchild.EffectiveColor);
    }

    [Fact]
    public async Task Handle_SetColor_StopsCascadeAtDescendantWithOwnColor()
    {
        var ct = Ct;
        var root = new Tag { Id = Guid.NewGuid(), Name = "root", Path = "root" };
        _db.InventoryTags.Add(root);
        await _db.SaveChangesAsync(ct);
        var child = new Tag { Id = Guid.NewGuid(), Name = "child", Path = "root/child", ParentId = root.Id, Color = "#22c55e", EffectiveColor = "#22c55e" };
        var grandchild = new Tag { Id = Guid.NewGuid(), Name = "leaf", Path = "root/child/leaf", ParentId = child.Id, EffectiveColor = "#22c55e" };
        _db.InventoryTags.AddRange(child, grandchild);
        await _db.SaveChangesAsync(ct);

        await PatchAsync(root.Id, p => p.Replace(m => m.Color, "#ff0000"));

        var reloadedChild = await _db.InventoryTags.SingleAsync(t => t.Id == child.Id, ct);
        var reloadedGrandchild = await _db.InventoryTags.SingleAsync(t => t.Id == grandchild.Id, ct);
        Assert.Equal("#22c55e", reloadedChild.EffectiveColor);
        Assert.Equal("#22c55e", reloadedGrandchild.EffectiveColor);
    }

    [Fact]
    public async Task Handle_WithDescriptionChange_UpdatesDescription()
    {
        var tag = new Tag { Id = Guid.NewGuid(), Name = "Test", Path = "Test", Description = "Old" };
        _db.InventoryTags.Add(tag);
        await _db.SaveChangesAsync(Ct);

        var resp = await PatchAsync(tag.Id, p => p.Replace(m => m.Description, "New description"));

        Assert.Equal("New description", resp.Description);
        _audit.Received(1).Track<InventoryTagSnapshot, Tag>(Arg.Any<Tag>(), "InventoryTag", tag.Id, Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_ClearDescription_SetsNull()
    {
        var tag = new Tag { Id = Guid.NewGuid(), Name = "Test", Path = "Test", Description = "Existing" };
        _db.InventoryTags.Add(tag);
        await _db.SaveChangesAsync(Ct);

        var resp = await PatchAsync(tag.Id, p => p.Replace(m => m.Description, null));

        Assert.Null(resp.Description);
    }

    [Fact]
    public async Task Handle_WithNameChange_SetsUpdatedAtAndUpdatedByUserId()
    {
        var tag = new Tag { Id = Guid.NewGuid(), Name = "Old", Path = "Old" };
        _db.InventoryTags.Add(tag);
        await _db.SaveChangesAsync(Ct);

        var before = DateTimeOffset.UtcNow;

        var resp = await PatchAsync(tag.Id, p => p.Replace(m => m.Name, "New"));

        Assert.NotNull(resp.UpdatedAt);
        Assert.True(resp.UpdatedAt >= before);
        Assert.NotNull(resp.UpdatedByUserId);
    }
}
