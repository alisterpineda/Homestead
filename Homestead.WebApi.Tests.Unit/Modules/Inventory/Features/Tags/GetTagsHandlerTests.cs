using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Tags.GetTags;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Features.Tags;

// The Search branch uses EF.Functions.ILike, which the EF InMemory provider cannot translate, so
// (like the Documents GetTagsHandler suite) it is not unit-tested here — only the ParentPath /
// IncludeChildren branch (StartsWith / ParentId, both InMemory-translatable) and sort/count are.
public class GetTagsHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly GetTagsHandler _sut;

    public GetTagsHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _sut = new GetTagsHandler(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Handle_DefaultSort_OrdersByNameAsc()
    {
        await SeedTagsAsync("Charlie", "Alpha", "Bravo");

        var result = await _sut.Handle(
            new GetInventoryTagsQuery { Page = 1, PageSize = 10 },
            TestContext.Current.CancellationToken);

        var names = result.Items.Select(t => t.Name).ToList();
        Assert.Equal(["Alpha", "Bravo", "Charlie"], names);
    }

    [Fact]
    public async Task Handle_SortByNameDesc_OrdersDescending()
    {
        await SeedTagsAsync("Alpha", "Bravo", "Charlie");

        var result = await _sut.Handle(
            new GetInventoryTagsQuery { Page = 1, PageSize = 10, SortBy = InventoryTagSortBy.Name, SortDir = SortDirection.Desc },
            TestContext.Current.CancellationToken);

        var names = result.Items.Select(t => t.Name).ToList();
        Assert.Equal(["Charlie", "Bravo", "Alpha"], names);
    }

    [Fact]
    public async Task Handle_SortByPathAsc_OrdersByPath()
    {
        _db.InventoryTags.Add(new Tag { Name = "C", Path = "Z/C" });
        _db.InventoryTags.Add(new Tag { Name = "A", Path = "A" });
        _db.InventoryTags.Add(new Tag { Name = "B", Path = "M/B" });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _sut.Handle(
            new GetInventoryTagsQuery { Page = 1, PageSize = 10, SortBy = InventoryTagSortBy.Path, SortDir = SortDirection.Asc },
            TestContext.Current.CancellationToken);

        var paths = result.Items.Select(t => t.Path).ToList();
        Assert.Equal(["A", "M/B", "Z/C"], paths);
    }

    [Fact]
    public async Task Handle_SortByPathDesc_OrdersDescending()
    {
        _db.InventoryTags.Add(new Tag { Name = "A", Path = "A" });
        _db.InventoryTags.Add(new Tag { Name = "B", Path = "M/B" });
        _db.InventoryTags.Add(new Tag { Name = "C", Path = "Z/C" });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _sut.Handle(
            new GetInventoryTagsQuery { Page = 1, PageSize = 10, SortBy = InventoryTagSortBy.Path, SortDir = SortDirection.Desc },
            TestContext.Current.CancellationToken);

        var paths = result.Items.Select(t => t.Path).ToList();
        Assert.Equal(["Z/C", "M/B", "A"], paths);
    }

    [Fact]
    public async Task Handle_SortByCreatedAtAsc_OrdersOldestFirst()
    {
        var now = DateTimeOffset.UtcNow;
        _db.InventoryTags.Add(new Tag { Name = "Third", Path = "Third", CreatedAt = now.AddHours(2) });
        _db.InventoryTags.Add(new Tag { Name = "First", Path = "First", CreatedAt = now });
        _db.InventoryTags.Add(new Tag { Name = "Second", Path = "Second", CreatedAt = now.AddHours(1) });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _sut.Handle(
            new GetInventoryTagsQuery { Page = 1, PageSize = 10, SortBy = InventoryTagSortBy.CreatedAt, SortDir = SortDirection.Asc },
            TestContext.Current.CancellationToken);

        var names = result.Items.Select(t => t.Name).ToList();
        Assert.Equal(["First", "Second", "Third"], names);
    }

    [Fact]
    public async Task Handle_SortByCreatedAtDesc_OrdersNewestFirst()
    {
        var now = DateTimeOffset.UtcNow;
        _db.InventoryTags.Add(new Tag { Name = "First", Path = "First", CreatedAt = now });
        _db.InventoryTags.Add(new Tag { Name = "Third", Path = "Third", CreatedAt = now.AddHours(2) });
        _db.InventoryTags.Add(new Tag { Name = "Second", Path = "Second", CreatedAt = now.AddHours(1) });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _sut.Handle(
            new GetInventoryTagsQuery { Page = 1, PageSize = 10, SortBy = InventoryTagSortBy.CreatedAt, SortDir = SortDirection.Desc },
            TestContext.Current.CancellationToken);

        var names = result.Items.Select(t => t.Name).ToList();
        Assert.Equal(["Third", "Second", "First"], names);
    }

    [Fact]
    public async Task Handle_SortByUpdatedAtAsc_OrdersOldestFirst()
    {
        var now = DateTimeOffset.UtcNow;
        _db.InventoryTags.Add(new Tag { Name = "Third", Path = "Third", UpdatedAt = now.AddHours(2) });
        _db.InventoryTags.Add(new Tag { Name = "First", Path = "First", UpdatedAt = now });
        _db.InventoryTags.Add(new Tag { Name = "Second", Path = "Second", UpdatedAt = now.AddHours(1) });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _sut.Handle(
            new GetInventoryTagsQuery { Page = 1, PageSize = 10, SortBy = InventoryTagSortBy.UpdatedAt, SortDir = SortDirection.Asc },
            TestContext.Current.CancellationToken);

        var names = result.Items.Select(t => t.Name).ToList();
        Assert.Equal(["First", "Second", "Third"], names);
    }

    [Fact]
    public async Task Handle_SortByUpdatedAtDesc_OrdersNewestFirst()
    {
        var now = DateTimeOffset.UtcNow;
        _db.InventoryTags.Add(new Tag { Name = "First", Path = "First", UpdatedAt = now });
        _db.InventoryTags.Add(new Tag { Name = "Third", Path = "Third", UpdatedAt = now.AddHours(2) });
        _db.InventoryTags.Add(new Tag { Name = "Second", Path = "Second", UpdatedAt = now.AddHours(1) });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _sut.Handle(
            new GetInventoryTagsQuery { Page = 1, PageSize = 10, SortBy = InventoryTagSortBy.UpdatedAt, SortDir = SortDirection.Desc },
            TestContext.Current.CancellationToken);

        var names = result.Items.Select(t => t.Name).ToList();
        Assert.Equal(["Third", "Second", "First"], names);
    }

    [Fact]
    public async Task Handle_NullSortBy_DefaultsToNameAsc()
    {
        await SeedTagsAsync("Zulu", "Alpha");

        var result = await _sut.Handle(
            new GetInventoryTagsQuery { Page = 1, PageSize = 10, SortBy = null, SortDir = null },
            TestContext.Current.CancellationToken);

        var names = result.Items.Select(t => t.Name).ToList();
        Assert.Equal(["Alpha", "Zulu"], names);
    }

    [Fact]
    public async Task Handle_ProjectsItemCount()
    {
        var ct = TestContext.Current.CancellationToken;
        var tagUsed = new Tag { Name = "Used", Path = "Used" };
        var tagUnused = new Tag { Name = "Unused", Path = "Unused" };
        _db.InventoryTags.AddRange(tagUsed, tagUnused);

        var itemA = await AddItemAsync("Drill");
        var itemB = await AddItemAsync("Saw");
        await _db.SaveChangesAsync(ct);

        _db.ItemTags.AddRange(
            new ItemTag { ItemId = itemA.Id, TagId = tagUsed.Id },
            new ItemTag { ItemId = itemB.Id, TagId = tagUsed.Id });
        await _db.SaveChangesAsync(ct);

        var result = await _sut.Handle(
            new GetInventoryTagsQuery { Page = 1, PageSize = 10 }, ct);

        var byName = result.Items.ToDictionary(t => t.Name, t => t.ItemCount);
        Assert.Equal(2, byName["Used"]);
        Assert.Equal(0, byName["Unused"]);
    }

    [Fact]
    public async Task Handle_SortByItemCountDesc_OrdersByMostItemsFirst()
    {
        var ct = TestContext.Current.CancellationToken;
        var tagFew = new Tag { Name = "Few", Path = "Few" };
        var tagMany = new Tag { Name = "Many", Path = "Many" };
        var tagNone = new Tag { Name = "None", Path = "None" };
        _db.InventoryTags.AddRange(tagFew, tagMany, tagNone);

        var itemA = await AddItemAsync("A");
        var itemB = await AddItemAsync("B");
        var itemC = await AddItemAsync("C");
        await _db.SaveChangesAsync(ct);

        _db.ItemTags.AddRange(
            new ItemTag { ItemId = itemA.Id, TagId = tagMany.Id },
            new ItemTag { ItemId = itemB.Id, TagId = tagMany.Id },
            new ItemTag { ItemId = itemC.Id, TagId = tagMany.Id },
            new ItemTag { ItemId = itemA.Id, TagId = tagFew.Id });
        await _db.SaveChangesAsync(ct);

        var result = await _sut.Handle(
            new GetInventoryTagsQuery { Page = 1, PageSize = 10, SortBy = InventoryTagSortBy.ItemCount, SortDir = SortDirection.Desc },
            ct);

        var names = result.Items.Select(t => t.Name).ToList();
        Assert.Equal("Many", names[0]);
        Assert.Equal("None", names[^1]);
    }

    [Fact]
    public async Task Handle_SortByItemCountAsc_OrdersByFewestItemsFirst()
    {
        var ct = TestContext.Current.CancellationToken;
        var tagWith = new Tag { Name = "WithItems", Path = "WithItems" };
        var tagWithout = new Tag { Name = "Without", Path = "Without" };
        _db.InventoryTags.AddRange(tagWith, tagWithout);

        var itemA = await AddItemAsync("A");
        var itemB = await AddItemAsync("B");
        await _db.SaveChangesAsync(ct);

        _db.ItemTags.AddRange(
            new ItemTag { ItemId = itemA.Id, TagId = tagWith.Id },
            new ItemTag { ItemId = itemB.Id, TagId = tagWith.Id });
        await _db.SaveChangesAsync(ct);

        var result = await _sut.Handle(
            new GetInventoryTagsQuery { Page = 1, PageSize = 10, SortBy = InventoryTagSortBy.ItemCount, SortDir = SortDirection.Asc },
            ct);

        var names = result.Items.Select(t => t.Name).ToList();
        Assert.Equal("Without", names[0]);
        Assert.Equal("WithItems", names[1]);
    }

    [Fact]
    public async Task Handle_ParentPath_DirectChildrenOnly()
    {
        var ct = TestContext.Current.CancellationToken;
        var tools = new Tag { Name = "tools", Path = "tools" };
        _db.InventoryTags.Add(tools);
        await _db.SaveChangesAsync(ct);
        _db.InventoryTags.AddRange(
            new Tag { Name = "power", Path = "tools/power", ParentId = tools.Id },
            new Tag { Name = "hand", Path = "tools/hand", ParentId = tools.Id });
        await _db.SaveChangesAsync(ct);
        // A grandchild — must NOT be returned by the direct-children query.
        var power = await _db.InventoryTags.FirstAsync(t => t.Path == "tools/power", ct);
        _db.InventoryTags.Add(new Tag { Name = "drill", Path = "tools/power/drill", ParentId = power.Id });
        await _db.SaveChangesAsync(ct);

        var result = await _sut.Handle(
            new GetInventoryTagsQuery { Page = 1, PageSize = 10, ParentPath = "tools", IncludeChildren = false },
            ct);

        var paths = result.Items.Select(t => t.Path).OrderBy(p => p).ToList();
        Assert.Equal(["tools/hand", "tools/power"], paths);
    }

    [Fact]
    public async Task Handle_ParentPath_IncludeChildren_ReturnsWholeSubtree()
    {
        var ct = TestContext.Current.CancellationToken;
        var tools = new Tag { Name = "tools", Path = "tools" };
        _db.InventoryTags.Add(tools);
        await _db.SaveChangesAsync(ct);
        var power = new Tag { Name = "power", Path = "tools/power", ParentId = tools.Id };
        _db.InventoryTags.Add(power);
        _db.InventoryTags.Add(new Tag { Name = "hand", Path = "tools/hand", ParentId = tools.Id });
        await _db.SaveChangesAsync(ct);
        _db.InventoryTags.Add(new Tag { Name = "drill", Path = "tools/power/drill", ParentId = power.Id });
        // An unrelated tag outside the subtree.
        _db.InventoryTags.Add(new Tag { Name = "kitchen", Path = "kitchen" });
        await _db.SaveChangesAsync(ct);

        var result = await _sut.Handle(
            new GetInventoryTagsQuery { Page = 1, PageSize = 10, ParentPath = "tools", IncludeChildren = true },
            ct);

        var paths = result.Items.Select(t => t.Path).OrderBy(p => p).ToList();
        // Whole subtree (StartsWith "tools/") but NOT "tools" itself nor "kitchen".
        Assert.Equal(["tools/hand", "tools/power", "tools/power/drill"], paths);
    }

    [Fact]
    public async Task Handle_ParentPath_UnknownParent_DirectChildren_ReturnsEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        _db.InventoryTags.Add(new Tag { Name = "tools", Path = "tools" });
        await _db.SaveChangesAsync(ct);

        var result = await _sut.Handle(
            new GetInventoryTagsQuery { Page = 1, PageSize = 10, ParentPath = "nonexistent", IncludeChildren = false },
            ct);

        Assert.Empty(result.Items);
    }

    private async Task<Item> AddItemAsync(string name)
    {
        var item = new Item
        {
            Id = Guid.NewGuid(),
            Name = name,
            Fungibility = Fungibility.NonFungible,
            Status = ItemStatus.Active,
            DurableKind = DurableKind.Plain,
        };
        _db.Items.Add(item);
        return await Task.FromResult(item);
    }

    private async Task SeedTagsAsync(params string[] names)
    {
        foreach (var name in names)
            _db.InventoryTags.Add(new Tag { Name = name, Path = name });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
