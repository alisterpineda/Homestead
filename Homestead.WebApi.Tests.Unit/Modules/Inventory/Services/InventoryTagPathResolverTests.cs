using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Inventory.Services;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Services;

public class InventoryTagPathResolverTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly InventoryTagPathResolver _sut;

    public InventoryTagPathResolverTests()
    {
        _db = TestAppDbContext.Create();
        _sut = new InventoryTagPathResolver(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task SingleSegment_CreatesOneTag_WithCorrectNameAndPath_NoParent()
    {
        var ct = TestContext.Current.CancellationToken;

        var tag = await _sut.ResolveOrCreateAsync("tools", ct: ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal("tools", tag.Name);
        Assert.Equal("tools", tag.Path);
        Assert.Null(tag.ParentId);
        Assert.Single(_db.InventoryTags);
    }

    [Fact]
    public async Task MultiSegment_CreatesFullHierarchy_WithCorrectParentChain()
    {
        var ct = TestContext.Current.CancellationToken;

        var leaf = await _sut.ResolveOrCreateAsync("tools/power", ct: ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal(2, _db.InventoryTags.Count());

        var parent = await _db.InventoryTags.SingleAsync(t => t.Path == "tools", ct);
        Assert.Equal("tools", parent.Name);
        Assert.Null(parent.ParentId);

        Assert.Equal("power", leaf.Name);
        Assert.Equal("tools/power", leaf.Path);
        Assert.Equal(parent.Id, leaf.ParentId);
    }

    [Fact]
    public async Task ThreeSegments_CreatesCorrectHierarchy()
    {
        var ct = TestContext.Current.CancellationToken;

        var leaf = await _sut.ResolveOrCreateAsync("tools/power/cordless", ct: ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal(3, _db.InventoryTags.Count());

        var root = await _db.InventoryTags.SingleAsync(t => t.Path == "tools", ct);
        var mid = await _db.InventoryTags.SingleAsync(t => t.Path == "tools/power", ct);

        Assert.Null(root.ParentId);
        Assert.Equal(root.Id, mid.ParentId);
        Assert.Equal(mid.Id, leaf.ParentId);
    }

    [Fact]
    public async Task ExistingTags_AreReused_NotDuplicated()
    {
        var ct = TestContext.Current.CancellationToken;

        await _sut.ResolveOrCreateAsync("tools/power", ct: ct);
        await _db.SaveChangesAsync(ct);

        var leaf = await _sut.ResolveOrCreateAsync("tools/hand", ct: ct);
        await _db.SaveChangesAsync(ct);

        // "tools" should exist only once, plus "power" and "hand"
        Assert.Equal(3, _db.InventoryTags.Count());

        var toolsTags = _db.InventoryTags.Where(t => t.Path == "tools").ToList();
        Assert.Single(toolsTags);
        Assert.Equal(toolsTags[0].Id, leaf.ParentId);
    }

    [Fact]
    public async Task PathNormalization_StripsLeadingAndTrailingSlashes()
    {
        var ct = TestContext.Current.CancellationToken;

        var tag = await _sut.ResolveOrCreateAsync("/tools/", ct: ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal("tools", tag.Path);
    }

    [Fact]
    public async Task PathNormalization_PreservesOriginalCasing()
    {
        var ct = TestContext.Current.CancellationToken;

        var tag = await _sut.ResolveOrCreateAsync("Tools/Power", ct: ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal("Tools/Power", tag.Path);
        Assert.Equal("Power", tag.Name);
    }

    [Fact]
    public async Task PathNormalization_CollapsesExtraSlashes()
    {
        var ct = TestContext.Current.CancellationToken;

        var tag = await _sut.ResolveOrCreateAsync("tools///power", ct: ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal("tools/power", tag.Path);
        Assert.Equal(2, _db.InventoryTags.Count());
    }

    [Fact]
    public async Task PathNormalization_TrimsWhitespace()
    {
        var ct = TestContext.Current.CancellationToken;

        var tag = await _sut.ResolveOrCreateAsync("  tools  ", ct: ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal("tools", tag.Path);
    }

    [Fact]
    public async Task LeafColor_AppliedOnlyToFinalSegment()
    {
        var ct = TestContext.Current.CancellationToken;

        var leaf = await _sut.ResolveOrCreateAsync("tools/power", leafColor: "#ff0000", ct: ct);
        await _db.SaveChangesAsync(ct);

        var parent = await _db.InventoryTags.SingleAsync(t => t.Path == "tools", ct);
        Assert.Null(parent.Color);
        Assert.Null(parent.EffectiveColor);
        Assert.Equal("#ff0000", leaf.Color);
        Assert.Equal("#ff0000", leaf.EffectiveColor);
    }

    [Fact]
    public async Task LeafColor_UpdatesExistingTagColor()
    {
        var ct = TestContext.Current.CancellationToken;

        await _sut.ResolveOrCreateAsync("tools", leafColor: "#ff0000", ct: ct);
        await _db.SaveChangesAsync(ct);

        var updated = await _sut.ResolveOrCreateAsync("tools", leafColor: "#00ff00", ct: ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal("#00ff00", updated.Color);
        Assert.Equal("#00ff00", updated.EffectiveColor);
        Assert.Single(_db.InventoryTags);
    }

    [Fact]
    public async Task LeafColor_RecolorExistingTag_CascadesToInheritingDescendants()
    {
        var ct = TestContext.Current.CancellationToken;

        // kitchen (coloured) → utensils → drawer; neither descendant has its own colour
        await _sut.ResolveOrCreateAsync("kitchen", leafColor: "#3b82f6", ct: ct);
        await _sut.ResolveOrCreateAsync("kitchen/utensils/drawer", ct: ct);
        await _db.SaveChangesAsync(ct);

        // Recolour the existing root via the upsert path
        await _sut.ResolveOrCreateAsync("kitchen", leafColor: "#ef4444", ct: ct);
        await _db.SaveChangesAsync(ct);

        var utensils = await _db.InventoryTags.SingleAsync(t => t.Path == "kitchen/utensils", ct);
        var drawer = await _db.InventoryTags.SingleAsync(t => t.Path == "kitchen/utensils/drawer", ct);

        Assert.Null(utensils.Color);
        Assert.Equal("#ef4444", utensils.EffectiveColor);
        Assert.Null(drawer.Color);
        Assert.Equal("#ef4444", drawer.EffectiveColor);
    }

    [Fact]
    public async Task LeafColor_RecolorExistingTag_StopsCascadeAtDescendantWithOwnColor()
    {
        var ct = TestContext.Current.CancellationToken;

        await _sut.ResolveOrCreateAsync("kitchen", leafColor: "#3b82f6", ct: ct);
        // utensils has its own colour; drawer inherits from utensils
        await _sut.ResolveOrCreateAsync("kitchen/utensils", leafColor: "#22c55e", ct: ct);
        await _sut.ResolveOrCreateAsync("kitchen/utensils/drawer", ct: ct);
        await _db.SaveChangesAsync(ct);

        // Recolour the root — the cascade must not cross a descendant that owns a colour
        await _sut.ResolveOrCreateAsync("kitchen", leafColor: "#ef4444", ct: ct);
        await _db.SaveChangesAsync(ct);

        var utensils = await _db.InventoryTags.SingleAsync(t => t.Path == "kitchen/utensils", ct);
        var drawer = await _db.InventoryTags.SingleAsync(t => t.Path == "kitchen/utensils/drawer", ct);

        Assert.Equal("#22c55e", utensils.Color);
        Assert.Equal("#22c55e", utensils.EffectiveColor);
        Assert.Equal("#22c55e", drawer.EffectiveColor);
    }

    [Fact]
    public async Task LeafColor_Null_DoesNotOverwriteExistingColor()
    {
        var ct = TestContext.Current.CancellationToken;

        await _sut.ResolveOrCreateAsync("tools", leafColor: "#ff0000", ct: ct);
        await _db.SaveChangesAsync(ct);

        var tag = await _sut.ResolveOrCreateAsync("tools", ct: ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal("#ff0000", tag.Color);
        Assert.Equal("#ff0000", tag.EffectiveColor);
    }

    [Fact]
    public async Task LeafDescription_UpdatesExistingLeafDescription()
    {
        var ct = TestContext.Current.CancellationToken;

        await _sut.ResolveOrCreateAsync("tools", leafDescription: "Original", ct: ct);
        await _db.SaveChangesAsync(ct);

        var tag = await _sut.ResolveOrCreateAsync("tools", leafDescription: "Updated", ct: ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal("Updated", tag.Description);
        Assert.Single(_db.InventoryTags);
    }

    [Fact]
    public async Task LeafDescription_AppliedOnlyToFinalSegment()
    {
        var ct = TestContext.Current.CancellationToken;

        var leaf = await _sut.ResolveOrCreateAsync("tools/power", leafDescription: "Cordless", ct: ct);
        await _db.SaveChangesAsync(ct);

        var parent = await _db.InventoryTags.SingleAsync(t => t.Path == "tools", ct);
        Assert.Null(parent.Description);
        Assert.Equal("Cordless", leaf.Description);
    }

    [Fact]
    public async Task RootTag_WithColor_EffectiveColorEqualsColor()
    {
        var ct = TestContext.Current.CancellationToken;

        var tag = await _sut.ResolveOrCreateAsync("kitchen", leafColor: "#3b82f6", ct: ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal("#3b82f6", tag.Color);
        Assert.Equal("#3b82f6", tag.EffectiveColor);
    }

    [Fact]
    public async Task RootTag_WithoutColor_EffectiveColorIsNull()
    {
        var ct = TestContext.Current.CancellationToken;

        var tag = await _sut.ResolveOrCreateAsync("kitchen", ct: ct);
        await _db.SaveChangesAsync(ct);

        Assert.Null(tag.Color);
        Assert.Null(tag.EffectiveColor);
    }

    [Fact]
    public async Task ChildTag_InheritsParentColor()
    {
        var ct = TestContext.Current.CancellationToken;

        await _sut.ResolveOrCreateAsync("kitchen", leafColor: "#3b82f6", ct: ct);
        await _db.SaveChangesAsync(ct);

        var child = await _sut.ResolveOrCreateAsync("kitchen/utensils", ct: ct);
        await _db.SaveChangesAsync(ct);

        Assert.Null(child.Color);
        Assert.Equal("#3b82f6", child.EffectiveColor);
    }

    [Fact]
    public async Task DeepInheritance_SkippingIntermediate()
    {
        var ct = TestContext.Current.CancellationToken;

        await _sut.ResolveOrCreateAsync("kitchen", leafColor: "#3b82f6", ct: ct);
        await _db.SaveChangesAsync(ct);

        // Create intermediate and grandchild — neither has own color
        var grandchild = await _sut.ResolveOrCreateAsync("kitchen/utensils/drawer", ct: ct);
        await _db.SaveChangesAsync(ct);

        var intermediate = await _db.InventoryTags.SingleAsync(t => t.Path == "kitchen/utensils", ct);

        Assert.Null(intermediate.Color);
        Assert.Equal("#3b82f6", intermediate.EffectiveColor);
        Assert.Null(grandchild.Color);
        Assert.Equal("#3b82f6", grandchild.EffectiveColor);
    }

    [Fact]
    public async Task ChildTag_WithOwnColor_IgnoresParent()
    {
        var ct = TestContext.Current.CancellationToken;

        await _sut.ResolveOrCreateAsync("kitchen", leafColor: "#3b82f6", ct: ct);
        await _db.SaveChangesAsync(ct);

        var child = await _sut.ResolveOrCreateAsync("kitchen/utensils", leafColor: "#ef4444", ct: ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal("#ef4444", child.Color);
        Assert.Equal("#ef4444", child.EffectiveColor);
    }

    [Fact]
    public async Task EmptyPath_ThrowsBusinessRule()
    {
        var ct = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.ResolveOrCreateAsync("", ct: ct));
    }

    [Fact]
    public async Task WhitespaceOnlyPath_ThrowsBusinessRule()
    {
        var ct = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.ResolveOrCreateAsync("   ", ct: ct));
    }

    [Fact]
    public async Task SlashOnlyPath_ThrowsBusinessRule()
    {
        var ct = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<BusinessRuleException>(() => _sut.ResolveOrCreateAsync("///", ct: ct));
    }

    [Fact]
    public async Task CreatedByUserId_SetOnNewTags()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();

        var tag = await _sut.ResolveOrCreateAsync("kitchen", createdByUserId: userId, ct: ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal(userId, tag.CreatedByUserId);
    }

    [Fact]
    public async Task CreatedByUserId_NotOverwrittenOnExistingTag()
    {
        var ct = TestContext.Current.CancellationToken;
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        await _sut.ResolveOrCreateAsync("kitchen", createdByUserId: userA, ct: ct);
        await _db.SaveChangesAsync(ct);

        var tag = await _sut.ResolveOrCreateAsync("kitchen", createdByUserId: userB, ct: ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal(userA, tag.CreatedByUserId);
    }

    [Fact]
    public async Task CreatedByUserId_SetOnIntermediateSegments()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();

        await _sut.ResolveOrCreateAsync("tools/power", createdByUserId: userId, ct: ct);
        await _db.SaveChangesAsync(ct);

        var parent = await _db.InventoryTags.SingleAsync(t => t.Path == "tools", ct);
        var leaf = await _db.InventoryTags.SingleAsync(t => t.Path == "tools/power", ct);

        Assert.Equal(userId, parent.CreatedByUserId);
        Assert.Equal(userId, leaf.CreatedByUserId);
    }

    // --- Batch (ResolveOrCreateManyAsync) tests ---

    [Fact]
    public async Task Batch_ResolvesMultiplePaths_SingleQuery()
    {
        var ct = TestContext.Current.CancellationToken;

        var tags = await _sut.ResolveOrCreateManyAsync(["kitchen", "tools"], ct: ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal(2, tags.Count);
        Assert.Equal("kitchen", tags[0].Path);
        Assert.Equal("tools", tags[1].Path);
        Assert.Equal(2, _db.InventoryTags.Count());
    }

    [Fact]
    public async Task Batch_SharedPrefix_CreatesParentOnce()
    {
        var ct = TestContext.Current.CancellationToken;

        var tags = await _sut.ResolveOrCreateManyAsync(["kitchen/utensils", "kitchen/appliances"], ct: ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal(2, tags.Count);
        Assert.Equal("kitchen/utensils", tags[0].Path);
        Assert.Equal("kitchen/appliances", tags[1].Path);
        Assert.Equal(3, _db.InventoryTags.Count());
        Assert.Single(_db.InventoryTags.Where(t => t.Path == "kitchen"));
    }

    [Fact]
    public async Task Batch_ReusesExistingTags()
    {
        var ct = TestContext.Current.CancellationToken;

        await _sut.ResolveOrCreateAsync("kitchen", ct: ct);
        await _db.SaveChangesAsync(ct);

        var tags = await _sut.ResolveOrCreateManyAsync(["kitchen/utensils", "kitchen/appliances"], ct: ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal(2, tags.Count);
        Assert.Equal(3, _db.InventoryTags.Count());
    }

    [Fact]
    public async Task Batch_EmptyList_ReturnsEmpty()
    {
        var ct = TestContext.Current.CancellationToken;

        var tags = await _sut.ResolveOrCreateManyAsync([], ct: ct);

        Assert.Empty(tags);
        Assert.Equal(0, _db.InventoryTags.Count());
    }

    [Fact]
    public async Task Batch_PreservesCreatedByUserId()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();

        var tags = await _sut.ResolveOrCreateManyAsync(["kitchen", "tools/power"], createdByUserId: userId, ct: ct);
        await _db.SaveChangesAsync(ct);

        Assert.All(_db.InventoryTags, t => Assert.Equal(userId, t.CreatedByUserId));
    }

    [Fact]
    public async Task Batch_InvalidPath_ThrowsBusinessRule()
    {
        var ct = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.ResolveOrCreateManyAsync(["kitchen", "///"], ct: ct));
    }

    [Fact]
    public async Task Batch_DuplicatePaths_ReturnsOneTagPerInput()
    {
        var ct = TestContext.Current.CancellationToken;

        var tags = await _sut.ResolveOrCreateManyAsync(["kitchen", "kitchen"], ct: ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal(2, tags.Count);
        Assert.Equal(tags[0].Id, tags[1].Id);
        Assert.Single(_db.InventoryTags);
    }

    [Fact]
    public async Task Batch_DeepHierarchy_CreatesFullChain()
    {
        var ct = TestContext.Current.CancellationToken;

        var tags = await _sut.ResolveOrCreateManyAsync(["a/b/c", "a/b/d"], ct: ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal(2, tags.Count);
        Assert.Equal("a/b/c", tags[0].Path);
        Assert.Equal("a/b/d", tags[1].Path);
        Assert.Equal(4, _db.InventoryTags.Count());

        var a = await _db.InventoryTags.SingleAsync(t => t.Path == "a", ct);
        var ab = await _db.InventoryTags.SingleAsync(t => t.Path == "a/b", ct);
        var abc = await _db.InventoryTags.SingleAsync(t => t.Path == "a/b/c", ct);
        var abd = await _db.InventoryTags.SingleAsync(t => t.Path == "a/b/d", ct);

        Assert.Null(a.ParentId);
        Assert.Equal(a.Id, ab.ParentId);
        Assert.Equal(ab.Id, abc.ParentId);
        Assert.Equal(ab.Id, abd.ParentId);
    }

    [Fact]
    public async Task Batch_ChangeTrackerTags_AreReused()
    {
        var ct = TestContext.Current.CancellationToken;

        // Resolve "kitchen" but do NOT save — it stays in the change tracker
        await _sut.ResolveOrCreateAsync("kitchen", ct: ct);

        var tags = await _sut.ResolveOrCreateManyAsync(["kitchen/utensils"], ct: ct);
        await _db.SaveChangesAsync(ct);

        Assert.Single(tags);
        Assert.Equal("kitchen/utensils", tags[0].Path);
        Assert.Equal(2, _db.InventoryTags.Count());
    }
}
