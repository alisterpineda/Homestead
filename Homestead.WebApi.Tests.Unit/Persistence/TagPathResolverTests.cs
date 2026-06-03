using Homestead.WebApi.Modules.Documents;
using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Tests.Unit.Persistence;

public class TagPathResolverTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly TagPathResolver _sut;

    public TagPathResolverTests()
    {
        _db = TestAppDbContext.Create();
        _sut = new TagPathResolver(_db);
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

        var tag = await _sut.ResolveOrCreateAsync("invoices", ct: ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal("invoices", tag.Name);
        Assert.Equal("invoices", tag.Path);
        Assert.Null(tag.ParentId);
        Assert.Single(_db.Tags);
    }

    [Fact]
    public async Task MultiSegment_CreatesFullHierarchy_WithCorrectParentChain()
    {
        var ct = TestContext.Current.CancellationToken;

        var leaf = await _sut.ResolveOrCreateAsync("legal/contracts", ct: ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal(2, _db.Tags.Count());

        var parent = await _db.Tags.SingleAsync(t => t.Path == "legal", ct);
        Assert.Equal("legal", parent.Name);
        Assert.Null(parent.ParentId);

        Assert.Equal("contracts", leaf.Name);
        Assert.Equal("legal/contracts", leaf.Path);
        Assert.Equal(parent.Id, leaf.ParentId);
    }

    [Fact]
    public async Task ThreeSegments_CreatesCorrectHierarchy()
    {
        var ct = TestContext.Current.CancellationToken;

        var leaf = await _sut.ResolveOrCreateAsync("legal/contracts/active", ct: ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal(3, _db.Tags.Count());

        var root = await _db.Tags.SingleAsync(t => t.Path == "legal", ct);
        var mid = await _db.Tags.SingleAsync(t => t.Path == "legal/contracts", ct);

        Assert.Null(root.ParentId);
        Assert.Equal(root.Id, mid.ParentId);
        Assert.Equal(mid.Id, leaf.ParentId);
    }

    [Fact]
    public async Task ExistingTags_AreReused_NotDuplicated()
    {
        var ct = TestContext.Current.CancellationToken;

        await _sut.ResolveOrCreateAsync("legal/contracts", ct: ct);
        await _db.SaveChangesAsync(ct);

        var leaf = await _sut.ResolveOrCreateAsync("legal/invoices", ct: ct);
        await _db.SaveChangesAsync(ct);

        // "legal" should exist only once, plus "contracts" and "invoices"
        Assert.Equal(3, _db.Tags.Count());

        var legalTags = _db.Tags.Where(t => t.Path == "legal").ToList();
        Assert.Single(legalTags);
        Assert.Equal(legalTags[0].Id, leaf.ParentId);
    }

    [Fact]
    public async Task PathNormalization_StripsLeadingAndTrailingSlashes()
    {
        var ct = TestContext.Current.CancellationToken;

        var tag = await _sut.ResolveOrCreateAsync("/invoices/", ct: ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal("invoices", tag.Path);
    }

    [Fact]
    public async Task PathNormalization_PreservesOriginalCasing()
    {
        var ct = TestContext.Current.CancellationToken;

        var tag = await _sut.ResolveOrCreateAsync("Legal/Contracts", ct: ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal("Legal/Contracts", tag.Path);
        Assert.Equal("Contracts", tag.Name);
    }

    [Fact]
    public async Task PathNormalization_CollapsesExtraSlashes()
    {
        var ct = TestContext.Current.CancellationToken;

        var tag = await _sut.ResolveOrCreateAsync("legal///contracts", ct: ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal("legal/contracts", tag.Path);
        Assert.Equal(2, _db.Tags.Count());
    }

    [Fact]
    public async Task PathNormalization_TrimsWhitespace()
    {
        var ct = TestContext.Current.CancellationToken;

        var tag = await _sut.ResolveOrCreateAsync("  invoices  ", ct: ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal("invoices", tag.Path);
    }

    [Fact]
    public async Task LeafColor_AppliedOnlyToFinalSegment()
    {
        var ct = TestContext.Current.CancellationToken;

        var leaf = await _sut.ResolveOrCreateAsync("legal/contracts", leafColor: "#ff0000", ct: ct);
        await _db.SaveChangesAsync(ct);

        var parent = await _db.Tags.SingleAsync(t => t.Path == "legal", ct);
        Assert.Null(parent.Color);
        Assert.Null(parent.EffectiveColor);
        Assert.Equal("#ff0000", leaf.Color);
        Assert.Equal("#ff0000", leaf.EffectiveColor);
    }

    [Fact]
    public async Task LeafColor_UpdatesExistingTagColor()
    {
        var ct = TestContext.Current.CancellationToken;

        await _sut.ResolveOrCreateAsync("invoices", leafColor: "#ff0000", ct: ct);
        await _db.SaveChangesAsync(ct);

        var updated = await _sut.ResolveOrCreateAsync("invoices", leafColor: "#00ff00", ct: ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal("#00ff00", updated.Color);
        Assert.Equal("#00ff00", updated.EffectiveColor);
        Assert.Single(_db.Tags);
    }

    [Fact]
    public async Task LeafColor_RecolorExistingTag_CascadesToInheritingDescendants()
    {
        var ct = TestContext.Current.CancellationToken;

        // finance (coloured) → invoices → 2024; neither descendant has its own colour
        await _sut.ResolveOrCreateAsync("finance", leafColor: "#3b82f6", ct: ct);
        await _sut.ResolveOrCreateAsync("finance/invoices/2024", ct: ct);
        await _db.SaveChangesAsync(ct);

        // Recolour the existing root via the upsert path
        await _sut.ResolveOrCreateAsync("finance", leafColor: "#ef4444", ct: ct);
        await _db.SaveChangesAsync(ct);

        var invoices = await _db.Tags.SingleAsync(t => t.Path == "finance/invoices", ct);
        var year = await _db.Tags.SingleAsync(t => t.Path == "finance/invoices/2024", ct);

        Assert.Null(invoices.Color);
        Assert.Equal("#ef4444", invoices.EffectiveColor);
        Assert.Null(year.Color);
        Assert.Equal("#ef4444", year.EffectiveColor);
    }

    [Fact]
    public async Task LeafColor_RecolorExistingTag_StopsCascadeAtDescendantWithOwnColor()
    {
        var ct = TestContext.Current.CancellationToken;

        await _sut.ResolveOrCreateAsync("finance", leafColor: "#3b82f6", ct: ct);
        // invoices has its own colour; 2024 inherits from invoices
        await _sut.ResolveOrCreateAsync("finance/invoices", leafColor: "#22c55e", ct: ct);
        await _sut.ResolveOrCreateAsync("finance/invoices/2024", ct: ct);
        await _db.SaveChangesAsync(ct);

        // Recolour the root — the cascade must not cross a descendant that owns a colour
        await _sut.ResolveOrCreateAsync("finance", leafColor: "#ef4444", ct: ct);
        await _db.SaveChangesAsync(ct);

        var invoices = await _db.Tags.SingleAsync(t => t.Path == "finance/invoices", ct);
        var year = await _db.Tags.SingleAsync(t => t.Path == "finance/invoices/2024", ct);

        Assert.Equal("#22c55e", invoices.Color);
        Assert.Equal("#22c55e", invoices.EffectiveColor);
        Assert.Equal("#22c55e", year.EffectiveColor);
    }

    [Fact]
    public async Task LeafColor_Null_DoesNotOverwriteExistingColor()
    {
        var ct = TestContext.Current.CancellationToken;

        await _sut.ResolveOrCreateAsync("invoices", leafColor: "#ff0000", ct: ct);
        await _db.SaveChangesAsync(ct);

        var tag = await _sut.ResolveOrCreateAsync("invoices", ct: ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal("#ff0000", tag.Color);
        Assert.Equal("#ff0000", tag.EffectiveColor);
    }

    [Fact]
    public async Task RootTag_WithColor_EffectiveColorEqualsColor()
    {
        var ct = TestContext.Current.CancellationToken;

        var tag = await _sut.ResolveOrCreateAsync("finance", leafColor: "#3b82f6", ct: ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal("#3b82f6", tag.Color);
        Assert.Equal("#3b82f6", tag.EffectiveColor);
    }

    [Fact]
    public async Task RootTag_WithoutColor_EffectiveColorIsNull()
    {
        var ct = TestContext.Current.CancellationToken;

        var tag = await _sut.ResolveOrCreateAsync("finance", ct: ct);
        await _db.SaveChangesAsync(ct);

        Assert.Null(tag.Color);
        Assert.Null(tag.EffectiveColor);
    }

    [Fact]
    public async Task ChildTag_InheritsParentColor()
    {
        var ct = TestContext.Current.CancellationToken;

        await _sut.ResolveOrCreateAsync("finance", leafColor: "#3b82f6", ct: ct);
        await _db.SaveChangesAsync(ct);

        var child = await _sut.ResolveOrCreateAsync("finance/invoices", ct: ct);
        await _db.SaveChangesAsync(ct);

        Assert.Null(child.Color);
        Assert.Equal("#3b82f6", child.EffectiveColor);
    }

    [Fact]
    public async Task DeepInheritance_SkippingIntermediate()
    {
        var ct = TestContext.Current.CancellationToken;

        await _sut.ResolveOrCreateAsync("finance", leafColor: "#3b82f6", ct: ct);
        await _db.SaveChangesAsync(ct);

        // Create intermediate and grandchild — neither has own color
        var grandchild = await _sut.ResolveOrCreateAsync("finance/invoices/2024", ct: ct);
        await _db.SaveChangesAsync(ct);

        var intermediate = await _db.Tags.SingleAsync(t => t.Path == "finance/invoices", ct);

        Assert.Null(intermediate.Color);
        Assert.Equal("#3b82f6", intermediate.EffectiveColor);
        Assert.Null(grandchild.Color);
        Assert.Equal("#3b82f6", grandchild.EffectiveColor);
    }

    [Fact]
    public async Task ChildTag_WithOwnColor_IgnoresParent()
    {
        var ct = TestContext.Current.CancellationToken;

        await _sut.ResolveOrCreateAsync("finance", leafColor: "#3b82f6", ct: ct);
        await _db.SaveChangesAsync(ct);

        var child = await _sut.ResolveOrCreateAsync("finance/invoices", leafColor: "#ef4444", ct: ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal("#ef4444", child.Color);
        Assert.Equal("#ef4444", child.EffectiveColor);
    }

    [Fact]
    public async Task EmptyPath_ThrowsArgumentException()
    {
        var ct = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.ResolveOrCreateAsync("", ct: ct));
    }

    [Fact]
    public async Task WhitespaceOnlyPath_ThrowsArgumentException()
    {
        var ct = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.ResolveOrCreateAsync("   ", ct: ct));
    }

    [Fact]
    public async Task SlashOnlyPath_ThrowsArgumentException()
    {
        var ct = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.ResolveOrCreateAsync("///", ct: ct));
    }

    [Fact]
    public async Task CreatedByUserId_SetOnNewTags()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();

        var tag = await _sut.ResolveOrCreateAsync("finance", createdByUserId: userId, ct: ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal(userId, tag.CreatedByUserId);
    }

    [Fact]
    public async Task CreatedByUserId_NotOverwrittenOnExistingTag()
    {
        var ct = TestContext.Current.CancellationToken;
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        await _sut.ResolveOrCreateAsync("finance", createdByUserId: userA, ct: ct);
        await _db.SaveChangesAsync(ct);

        var tag = await _sut.ResolveOrCreateAsync("finance", createdByUserId: userB, ct: ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal(userA, tag.CreatedByUserId);
    }

    [Fact]
    public async Task CreatedByUserId_SetOnIntermediateSegments()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();

        await _sut.ResolveOrCreateAsync("legal/contracts", createdByUserId: userId, ct: ct);
        await _db.SaveChangesAsync(ct);

        var parent = await _db.Tags.SingleAsync(t => t.Path == "legal", ct);
        var leaf = await _db.Tags.SingleAsync(t => t.Path == "legal/contracts", ct);

        Assert.Equal(userId, parent.CreatedByUserId);
        Assert.Equal(userId, leaf.CreatedByUserId);
    }

    // --- Batch (ResolveOrCreateManyAsync) tests ---

    [Fact]
    public async Task Batch_ResolvesMultiplePaths_SingleQuery()
    {
        var ct = TestContext.Current.CancellationToken;

        var tags = await _sut.ResolveOrCreateManyAsync(["finance", "legal"], ct: ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal(2, tags.Count);
        Assert.Equal("finance", tags[0].Path);
        Assert.Equal("legal", tags[1].Path);
        Assert.Equal(2, _db.Tags.Count());
    }

    [Fact]
    public async Task Batch_SharedPrefix_CreatesParentOnce()
    {
        var ct = TestContext.Current.CancellationToken;

        var tags = await _sut.ResolveOrCreateManyAsync(["finance/invoices", "finance/contracts"], ct: ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal(2, tags.Count);
        Assert.Equal("finance/invoices", tags[0].Path);
        Assert.Equal("finance/contracts", tags[1].Path);
        Assert.Equal(3, _db.Tags.Count());
        Assert.Single(_db.Tags.Where(t => t.Path == "finance"));
    }

    [Fact]
    public async Task Batch_ReusesExistingTags()
    {
        var ct = TestContext.Current.CancellationToken;

        await _sut.ResolveOrCreateAsync("finance", ct: ct);
        await _db.SaveChangesAsync(ct);

        var tags = await _sut.ResolveOrCreateManyAsync(["finance/invoices", "finance/contracts"], ct: ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal(2, tags.Count);
        Assert.Equal(3, _db.Tags.Count());
    }

    [Fact]
    public async Task Batch_EmptyList_ReturnsEmpty()
    {
        var ct = TestContext.Current.CancellationToken;

        var tags = await _sut.ResolveOrCreateManyAsync([], ct: ct);

        Assert.Empty(tags);
        Assert.Equal(0, _db.Tags.Count());
    }

    [Fact]
    public async Task Batch_PreservesCreatedByUserId()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();

        var tags = await _sut.ResolveOrCreateManyAsync(["finance", "legal/contracts"], createdByUserId: userId, ct: ct);
        await _db.SaveChangesAsync(ct);

        Assert.All(_db.Tags, t => Assert.Equal(userId, t.CreatedByUserId));
    }

    [Fact]
    public async Task Batch_InvalidPath_ThrowsArgumentException()
    {
        var ct = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.ResolveOrCreateManyAsync(["finance", "///"], ct: ct));
    }

    [Fact]
    public async Task Batch_DuplicatePaths_ReturnsOneTagPerInput()
    {
        var ct = TestContext.Current.CancellationToken;

        var tags = await _sut.ResolveOrCreateManyAsync(["finance", "finance"], ct: ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal(2, tags.Count);
        Assert.Equal(tags[0].Id, tags[1].Id);
        Assert.Single(_db.Tags);
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
        Assert.Equal(4, _db.Tags.Count());

        var a = await _db.Tags.SingleAsync(t => t.Path == "a", ct);
        var ab = await _db.Tags.SingleAsync(t => t.Path == "a/b", ct);
        var abc = await _db.Tags.SingleAsync(t => t.Path == "a/b/c", ct);
        var abd = await _db.Tags.SingleAsync(t => t.Path == "a/b/d", ct);

        Assert.Null(a.ParentId);
        Assert.Equal(a.Id, ab.ParentId);
        Assert.Equal(ab.Id, abc.ParentId);
        Assert.Equal(ab.Id, abd.ParentId);
    }

    [Fact]
    public async Task Batch_ChangeTrackerTags_AreReused()
    {
        var ct = TestContext.Current.CancellationToken;

        // Resolve "finance" but do NOT save — it stays in the change tracker
        await _sut.ResolveOrCreateAsync("finance", ct: ct);

        var tags = await _sut.ResolveOrCreateManyAsync(["finance/invoices"], ct: ct);
        await _db.SaveChangesAsync(ct);

        Assert.Single(tags);
        Assert.Equal("finance/invoices", tags[0].Path);
        Assert.Equal(2, _db.Tags.Count());
    }
}
