using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Documents;
using Homestead.WebApi.Domains.Parties;
using Homestead.WebApi.Features.Documents.v1.GetParties;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Documents;

public class GetPartiesHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IDocumentQueryService _accessService;
    private readonly GetPartiesHandler _sut;

    public GetPartiesHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _accessService = Substitute.For<IDocumentQueryService>();
        _accessService.IsAdmin.Returns(true);
        _sut = new GetPartiesHandler(_db, _accessService);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task HandleAsync_DefaultSort_OrdersByNameAsc()
    {
        await SeedPartiesAsync("Charlie", "Alpha", "Bravo");

        var result = await _sut.HandleAsync(
            new GetPartiesRequest { Page = 1, PageSize = 10 },
            CancellationToken.None);

        var names = result.Items.Select(p => p.Name).ToList();
        Assert.Equal(["Alpha", "Bravo", "Charlie"], names);
    }

    [Fact]
    public async Task HandleAsync_SortByNameDesc_OrdersDescending()
    {
        await SeedPartiesAsync("Alpha", "Bravo", "Charlie");

        var result = await _sut.HandleAsync(
            new GetPartiesRequest { Page = 1, PageSize = 10, SortBy = PartySortBy.Name, SortDir = SortDirection.Desc },
            CancellationToken.None);

        var names = result.Items.Select(p => p.Name).ToList();
        Assert.Equal(["Charlie", "Bravo", "Alpha"], names);
    }

    [Fact]
    public async Task HandleAsync_SortByCreatedAtAsc_OrdersOldestFirst()
    {
        var now = DateTimeOffset.UtcNow;
        _db.Parties.Add(new Party { Name = "Third", CreatedAt = now.AddHours(2) });
        _db.Parties.Add(new Party { Name = "First", CreatedAt = now });
        _db.Parties.Add(new Party { Name = "Second", CreatedAt = now.AddHours(1) });
        await _db.SaveChangesAsync();

        var result = await _sut.HandleAsync(
            new GetPartiesRequest { Page = 1, PageSize = 10, SortBy = PartySortBy.CreatedAt, SortDir = SortDirection.Asc },
            CancellationToken.None);

        var names = result.Items.Select(p => p.Name).ToList();
        Assert.Equal(["First", "Second", "Third"], names);
    }

    [Fact]
    public async Task HandleAsync_SortByCreatedAtDesc_OrdersNewestFirst()
    {
        var now = DateTimeOffset.UtcNow;
        _db.Parties.Add(new Party { Name = "First", CreatedAt = now });
        _db.Parties.Add(new Party { Name = "Third", CreatedAt = now.AddHours(2) });
        _db.Parties.Add(new Party { Name = "Second", CreatedAt = now.AddHours(1) });
        await _db.SaveChangesAsync();

        var result = await _sut.HandleAsync(
            new GetPartiesRequest { Page = 1, PageSize = 10, SortBy = PartySortBy.CreatedAt, SortDir = SortDirection.Desc },
            CancellationToken.None);

        var names = result.Items.Select(p => p.Name).ToList();
        Assert.Equal(["Third", "Second", "First"], names);
    }

    [Fact]
    public async Task HandleAsync_SortByDocumentCountDesc_OrdersByMostDocumentsFirst()
    {
        var docA = new Document { Id = Guid.NewGuid(), Name = "DocA" };
        var docB = new Document { Id = Guid.NewGuid(), Name = "DocB" };
        var docC = new Document { Id = Guid.NewGuid(), Name = "DocC" };
        _db.Documents.AddRange(docA, docB, docC);

        var partyFew = new Party { Name = "Few" };
        var partyMany = new Party { Name = "Many" };
        var partyNone = new Party { Name = "None" };
        _db.Parties.AddRange(partyFew, partyMany, partyNone);
        await _db.SaveChangesAsync();

        _db.Set<DocumentParty>().AddRange(
            new DocumentParty { DocumentId = docA.Id, PartyId = partyMany.Id },
            new DocumentParty { DocumentId = docB.Id, PartyId = partyMany.Id },
            new DocumentParty { DocumentId = docC.Id, PartyId = partyMany.Id },
            new DocumentParty { DocumentId = docA.Id, PartyId = partyFew.Id }
        );
        await _db.SaveChangesAsync();

        var result = await _sut.HandleAsync(
            new GetPartiesRequest { Page = 1, PageSize = 10, SortBy = PartySortBy.DocumentCount, SortDir = SortDirection.Desc },
            CancellationToken.None);

        var names = result.Items.Select(p => p.Name).ToList();
        Assert.Equal("Many", names[0]);
        Assert.Equal("None", names[^1]);
    }

    [Fact]
    public async Task HandleAsync_SortByDocumentCountAsc_OrdersByFewestDocumentsFirst()
    {
        var docA = new Document { Id = Guid.NewGuid(), Name = "DocA" };
        var docB = new Document { Id = Guid.NewGuid(), Name = "DocB" };
        _db.Documents.AddRange(docA, docB);

        var partyWithDocs = new Party { Name = "WithDocs" };
        var partyWithout = new Party { Name = "Without" };
        _db.Parties.AddRange(partyWithDocs, partyWithout);
        await _db.SaveChangesAsync();

        _db.Set<DocumentParty>().AddRange(
            new DocumentParty { DocumentId = docA.Id, PartyId = partyWithDocs.Id },
            new DocumentParty { DocumentId = docB.Id, PartyId = partyWithDocs.Id }
        );
        await _db.SaveChangesAsync();

        var result = await _sut.HandleAsync(
            new GetPartiesRequest { Page = 1, PageSize = 10, SortBy = PartySortBy.DocumentCount, SortDir = SortDirection.Asc },
            CancellationToken.None);

        var names = result.Items.Select(p => p.Name).ToList();
        Assert.Equal("Without", names[0]);
        Assert.Equal("WithDocs", names[1]);
    }

    [Fact]
    public async Task HandleAsync_NullSortBy_DefaultsToNameAsc()
    {
        await SeedPartiesAsync("Zulu", "Alpha");

        var result = await _sut.HandleAsync(
            new GetPartiesRequest { Page = 1, PageSize = 10, SortBy = null, SortDir = null },
            CancellationToken.None);

        var names = result.Items.Select(p => p.Name).ToList();
        Assert.Equal(["Alpha", "Zulu"], names);
    }

    [Fact]
    public async Task HandleAsync_SortByNameAsc_WithSearch_FiltersAndSorts()
    {
        await SeedPartiesAsync("Banana Corp", "Apple Inc", "Cherry LLC", "Avocado Ltd");

        var result = await _sut.HandleAsync(
            new GetPartiesRequest { Page = 1, PageSize = 10, Search = "a", SortBy = PartySortBy.Name, SortDir = SortDirection.Asc },
            CancellationToken.None);

        var names = result.Items.Select(p => p.Name).ToList();
        // All contain "a" (case-insensitive), sorted by name ascending
        Assert.True(names.SequenceEqual(names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)));
        foreach (var name in names)
            Assert.Contains("a", name, StringComparison.OrdinalIgnoreCase);
    }

    private async Task SeedPartiesAsync(params string[] names)
    {
        foreach (var name in names)
            _db.Parties.Add(new Party { Name = name });
        await _db.SaveChangesAsync();
    }
}
