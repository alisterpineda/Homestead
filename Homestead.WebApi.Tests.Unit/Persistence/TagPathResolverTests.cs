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
        Assert.Equal("#ff0000", leaf.Color);
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
        Assert.Single(_db.Tags);
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
}
