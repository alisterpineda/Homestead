using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Modules.Documents.Features.Tags.GetTags;

namespace Homestead.WebApi.Tests.Unit.Documents;

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

        var result = await _sut.HandleAsync(
            new GetTagsQuery { Page = 1, PageSize = 10 },
            TestContext.Current.CancellationToken);

        var names = result.Items.Select(t => t.Name).ToList();
        Assert.Equal(["Alpha", "Bravo", "Charlie"], names);
    }

    [Fact]
    public async Task Handle_SortByNameDesc_OrdersDescending()
    {
        await SeedTagsAsync("Alpha", "Bravo", "Charlie");

        var result = await _sut.HandleAsync(
            new GetTagsQuery { Page = 1, PageSize = 10, SortBy = TagSortBy.Name, SortDir = SortDirection.Desc },
            TestContext.Current.CancellationToken);

        var names = result.Items.Select(t => t.Name).ToList();
        Assert.Equal(["Charlie", "Bravo", "Alpha"], names);
    }

    [Fact]
    public async Task Handle_SortByPathAsc_OrdersByPath()
    {
        _db.Tags.Add(new Tag { Name = "C", Path = "Z/C" });
        _db.Tags.Add(new Tag { Name = "A", Path = "A" });
        _db.Tags.Add(new Tag { Name = "B", Path = "M/B" });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _sut.HandleAsync(
            new GetTagsQuery { Page = 1, PageSize = 10, SortBy = TagSortBy.Path, SortDir = SortDirection.Asc },
            TestContext.Current.CancellationToken);

        var paths = result.Items.Select(t => t.Path).ToList();
        Assert.Equal(["A", "M/B", "Z/C"], paths);
    }

    [Fact]
    public async Task Handle_SortByPathDesc_OrdersDescending()
    {
        _db.Tags.Add(new Tag { Name = "A", Path = "A" });
        _db.Tags.Add(new Tag { Name = "B", Path = "M/B" });
        _db.Tags.Add(new Tag { Name = "C", Path = "Z/C" });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _sut.HandleAsync(
            new GetTagsQuery { Page = 1, PageSize = 10, SortBy = TagSortBy.Path, SortDir = SortDirection.Desc },
            TestContext.Current.CancellationToken);

        var paths = result.Items.Select(t => t.Path).ToList();
        Assert.Equal(["Z/C", "M/B", "A"], paths);
    }

    [Fact]
    public async Task Handle_SortByCreatedAtAsc_OrdersOldestFirst()
    {
        var now = DateTimeOffset.UtcNow;
        _db.Tags.Add(new Tag { Name = "Third", Path = "Third", CreatedAt = now.AddHours(2) });
        _db.Tags.Add(new Tag { Name = "First", Path = "First", CreatedAt = now });
        _db.Tags.Add(new Tag { Name = "Second", Path = "Second", CreatedAt = now.AddHours(1) });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _sut.HandleAsync(
            new GetTagsQuery { Page = 1, PageSize = 10, SortBy = TagSortBy.CreatedAt, SortDir = SortDirection.Asc },
            TestContext.Current.CancellationToken);

        var names = result.Items.Select(t => t.Name).ToList();
        Assert.Equal(["First", "Second", "Third"], names);
    }

    [Fact]
    public async Task Handle_SortByCreatedAtDesc_OrdersNewestFirst()
    {
        var now = DateTimeOffset.UtcNow;
        _db.Tags.Add(new Tag { Name = "First", Path = "First", CreatedAt = now });
        _db.Tags.Add(new Tag { Name = "Third", Path = "Third", CreatedAt = now.AddHours(2) });
        _db.Tags.Add(new Tag { Name = "Second", Path = "Second", CreatedAt = now.AddHours(1) });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _sut.HandleAsync(
            new GetTagsQuery { Page = 1, PageSize = 10, SortBy = TagSortBy.CreatedAt, SortDir = SortDirection.Desc },
            TestContext.Current.CancellationToken);

        var names = result.Items.Select(t => t.Name).ToList();
        Assert.Equal(["Third", "Second", "First"], names);
    }

    [Fact]
    public async Task Handle_SortByDocumentCountDesc_OrdersByMostDocumentsFirst()
    {
        var ct = TestContext.Current.CancellationToken;
        var docA = new Document { Id = Guid.NewGuid(), Name = "DocA" };
        var docB = new Document { Id = Guid.NewGuid(), Name = "DocB" };
        var docC = new Document { Id = Guid.NewGuid(), Name = "DocC" };
        _db.Documents.AddRange(docA, docB, docC);

        var tagFew = new Tag { Name = "Few", Path = "Few" };
        var tagMany = new Tag { Name = "Many", Path = "Many" };
        var tagNone = new Tag { Name = "None", Path = "None" };
        _db.Tags.AddRange(tagFew, tagMany, tagNone);
        await _db.SaveChangesAsync(ct);

        _db.Set<DocumentTag>().AddRange(
            new DocumentTag { DocumentId = docA.Id, TagId = tagMany.Id },
            new DocumentTag { DocumentId = docB.Id, TagId = tagMany.Id },
            new DocumentTag { DocumentId = docC.Id, TagId = tagMany.Id },
            new DocumentTag { DocumentId = docA.Id, TagId = tagFew.Id }
        );
        await _db.SaveChangesAsync(ct);

        var result = await _sut.HandleAsync(
            new GetTagsQuery { Page = 1, PageSize = 10, SortBy = TagSortBy.DocumentCount, SortDir = SortDirection.Desc },
            ct);

        var names = result.Items.Select(t => t.Name).ToList();
        Assert.Equal("Many", names[0]);
        Assert.Equal("None", names[^1]);
    }

    [Fact]
    public async Task Handle_SortByDocumentCountAsc_OrdersByFewestDocumentsFirst()
    {
        var ct = TestContext.Current.CancellationToken;
        var docA = new Document { Id = Guid.NewGuid(), Name = "DocA" };
        var docB = new Document { Id = Guid.NewGuid(), Name = "DocB" };
        _db.Documents.AddRange(docA, docB);

        var tagWithDocs = new Tag { Name = "WithDocs", Path = "WithDocs" };
        var tagWithout = new Tag { Name = "Without", Path = "Without" };
        _db.Tags.AddRange(tagWithDocs, tagWithout);
        await _db.SaveChangesAsync(ct);

        _db.Set<DocumentTag>().AddRange(
            new DocumentTag { DocumentId = docA.Id, TagId = tagWithDocs.Id },
            new DocumentTag { DocumentId = docB.Id, TagId = tagWithDocs.Id }
        );
        await _db.SaveChangesAsync(ct);

        var result = await _sut.HandleAsync(
            new GetTagsQuery { Page = 1, PageSize = 10, SortBy = TagSortBy.DocumentCount, SortDir = SortDirection.Asc },
            ct);

        var names = result.Items.Select(t => t.Name).ToList();
        Assert.Equal("Without", names[0]);
        Assert.Equal("WithDocs", names[1]);
    }

    [Fact]
    public async Task Handle_SortByUpdatedAtAsc_OrdersOldestFirst()
    {
        var now = DateTimeOffset.UtcNow;
        _db.Tags.Add(new Tag { Name = "Third", Path = "Third", UpdatedAt = now.AddHours(2) });
        _db.Tags.Add(new Tag { Name = "First", Path = "First", UpdatedAt = now });
        _db.Tags.Add(new Tag { Name = "Second", Path = "Second", UpdatedAt = now.AddHours(1) });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _sut.HandleAsync(
            new GetTagsQuery { Page = 1, PageSize = 10, SortBy = TagSortBy.UpdatedAt, SortDir = SortDirection.Asc },
            TestContext.Current.CancellationToken);

        var names = result.Items.Select(t => t.Name).ToList();
        Assert.Equal(["First", "Second", "Third"], names);
    }

    [Fact]
    public async Task Handle_SortByUpdatedAtDesc_OrdersNewestFirst()
    {
        var now = DateTimeOffset.UtcNow;
        _db.Tags.Add(new Tag { Name = "First", Path = "First", UpdatedAt = now });
        _db.Tags.Add(new Tag { Name = "Third", Path = "Third", UpdatedAt = now.AddHours(2) });
        _db.Tags.Add(new Tag { Name = "Second", Path = "Second", UpdatedAt = now.AddHours(1) });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _sut.HandleAsync(
            new GetTagsQuery { Page = 1, PageSize = 10, SortBy = TagSortBy.UpdatedAt, SortDir = SortDirection.Desc },
            TestContext.Current.CancellationToken);

        var names = result.Items.Select(t => t.Name).ToList();
        Assert.Equal(["Third", "Second", "First"], names);
    }

    [Fact]
    public async Task Handle_NullSortBy_DefaultsToNameAsc()
    {
        await SeedTagsAsync("Zulu", "Alpha");

        var result = await _sut.HandleAsync(
            new GetTagsQuery { Page = 1, PageSize = 10, SortBy = null, SortDir = null },
            TestContext.Current.CancellationToken);

        var names = result.Items.Select(t => t.Name).ToList();
        Assert.Equal(["Alpha", "Zulu"], names);
    }

    private async Task SeedTagsAsync(params string[] names)
    {
        foreach (var name in names)
            _db.Tags.Add(new Tag { Name = name, Path = name });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
