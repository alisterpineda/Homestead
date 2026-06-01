using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Domain.Parties;
using Homestead.WebApi.Modules.Documents.Features.Parties.GetParties;
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
    public async Task Handle_DefaultSort_OrdersByNameAsc()
    {
        await SeedPartiesAsync("Charlie", "Alpha", "Bravo");

        var result = await _sut.Handle(
            new GetPartiesQuery { Page = 1, PageSize = 10 },
            TestContext.Current.CancellationToken);

        var names = result.Items.Select(p => p.Name).ToList();
        Assert.Equal(["Alpha", "Bravo", "Charlie"], names);
    }

    [Fact]
    public async Task Handle_SortByNameDesc_OrdersDescending()
    {
        await SeedPartiesAsync("Alpha", "Bravo", "Charlie");

        var result = await _sut.Handle(
            new GetPartiesQuery { Page = 1, PageSize = 10, SortBy = PartySortBy.Name, SortDir = SortDirection.Desc },
            TestContext.Current.CancellationToken);

        var names = result.Items.Select(p => p.Name).ToList();
        Assert.Equal(["Charlie", "Bravo", "Alpha"], names);
    }

    [Fact]
    public async Task Handle_SortByCreatedAtAsc_OrdersOldestFirst()
    {
        var now = DateTimeOffset.UtcNow;
        _db.Parties.Add(new Party { Name = "Third", CreatedAt = now.AddHours(2) });
        _db.Parties.Add(new Party { Name = "First", CreatedAt = now });
        _db.Parties.Add(new Party { Name = "Second", CreatedAt = now.AddHours(1) });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _sut.Handle(
            new GetPartiesQuery { Page = 1, PageSize = 10, SortBy = PartySortBy.CreatedAt, SortDir = SortDirection.Asc },
            TestContext.Current.CancellationToken);

        var names = result.Items.Select(p => p.Name).ToList();
        Assert.Equal(["First", "Second", "Third"], names);
    }

    [Fact]
    public async Task Handle_SortByCreatedAtDesc_OrdersNewestFirst()
    {
        var now = DateTimeOffset.UtcNow;
        _db.Parties.Add(new Party { Name = "First", CreatedAt = now });
        _db.Parties.Add(new Party { Name = "Third", CreatedAt = now.AddHours(2) });
        _db.Parties.Add(new Party { Name = "Second", CreatedAt = now.AddHours(1) });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _sut.Handle(
            new GetPartiesQuery { Page = 1, PageSize = 10, SortBy = PartySortBy.CreatedAt, SortDir = SortDirection.Desc },
            TestContext.Current.CancellationToken);

        var names = result.Items.Select(p => p.Name).ToList();
        Assert.Equal(["Third", "Second", "First"], names);
    }

    [Fact]
    public async Task Handle_SortByUpdatedAtAsc_OrdersOldestFirst()
    {
        var now = DateTimeOffset.UtcNow;
        _db.Parties.Add(new Party { Name = "Third", UpdatedAt = now.AddHours(2) });
        _db.Parties.Add(new Party { Name = "First", UpdatedAt = now });
        _db.Parties.Add(new Party { Name = "Second", UpdatedAt = now.AddHours(1) });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _sut.Handle(
            new GetPartiesQuery { Page = 1, PageSize = 10, SortBy = PartySortBy.UpdatedAt, SortDir = SortDirection.Asc },
            TestContext.Current.CancellationToken);

        var names = result.Items.Select(p => p.Name).ToList();
        Assert.Equal(["First", "Second", "Third"], names);
    }

    [Fact]
    public async Task Handle_SortByUpdatedAtDesc_OrdersNewestFirst()
    {
        var now = DateTimeOffset.UtcNow;
        _db.Parties.Add(new Party { Name = "First", UpdatedAt = now });
        _db.Parties.Add(new Party { Name = "Third", UpdatedAt = now.AddHours(2) });
        _db.Parties.Add(new Party { Name = "Second", UpdatedAt = now.AddHours(1) });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _sut.Handle(
            new GetPartiesQuery { Page = 1, PageSize = 10, SortBy = PartySortBy.UpdatedAt, SortDir = SortDirection.Desc },
            TestContext.Current.CancellationToken);

        var names = result.Items.Select(p => p.Name).ToList();
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

        var partyFew = new Party { Name = "Few" };
        var partyMany = new Party { Name = "Many" };
        var partyNone = new Party { Name = "None" };
        _db.Parties.AddRange(partyFew, partyMany, partyNone);
        await _db.SaveChangesAsync(ct);

        _db.Set<DocumentParty>().AddRange(
            new DocumentParty { DocumentId = docA.Id, PartyId = partyMany.Id },
            new DocumentParty { DocumentId = docB.Id, PartyId = partyMany.Id },
            new DocumentParty { DocumentId = docC.Id, PartyId = partyMany.Id },
            new DocumentParty { DocumentId = docA.Id, PartyId = partyFew.Id }
        );
        await _db.SaveChangesAsync(ct);

        var result = await _sut.Handle(
            new GetPartiesQuery { Page = 1, PageSize = 10, SortBy = PartySortBy.DocumentCount, SortDir = SortDirection.Desc },
            ct);

        var names = result.Items.Select(p => p.Name).ToList();
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

        var partyWithDocs = new Party { Name = "WithDocs" };
        var partyWithout = new Party { Name = "Without" };
        _db.Parties.AddRange(partyWithDocs, partyWithout);
        await _db.SaveChangesAsync(ct);

        _db.Set<DocumentParty>().AddRange(
            new DocumentParty { DocumentId = docA.Id, PartyId = partyWithDocs.Id },
            new DocumentParty { DocumentId = docB.Id, PartyId = partyWithDocs.Id }
        );
        await _db.SaveChangesAsync(ct);

        var result = await _sut.Handle(
            new GetPartiesQuery { Page = 1, PageSize = 10, SortBy = PartySortBy.DocumentCount, SortDir = SortDirection.Asc },
            ct);

        var names = result.Items.Select(p => p.Name).ToList();
        Assert.Equal("Without", names[0]);
        Assert.Equal("WithDocs", names[1]);
    }

    [Fact]
    public async Task Handle_NullSortBy_DefaultsToNameAsc()
    {
        await SeedPartiesAsync("Zulu", "Alpha");

        var result = await _sut.Handle(
            new GetPartiesQuery { Page = 1, PageSize = 10, SortBy = null, SortDir = null },
            TestContext.Current.CancellationToken);

        var names = result.Items.Select(p => p.Name).ToList();
        Assert.Equal(["Alpha", "Zulu"], names);
    }

    [Fact]
    public async Task Handle_SortByNameAsc_WithSearch_FiltersAndSorts()
    {
        await SeedPartiesAsync("Banana Corp", "Apple Inc", "Cherry LLC", "Avocado Ltd");

        var result = await _sut.Handle(
            new GetPartiesQuery { Page = 1, PageSize = 10, Search = "a", SortBy = PartySortBy.Name, SortDir = SortDirection.Asc },
            TestContext.Current.CancellationToken);

        var names = result.Items.Select(p => p.Name).ToList();
        // GetPartiesHandler filters with Name.ToLower().Contains(search) (NOT EF.Functions.ILike). On EF
        // InMemory that runs as CLR string ops; on PostgreSQL it lowercases both sides under the DB collation.
        // With all-ASCII fixtures this only pins the query SHAPE / case-folding intent — it is NOT coverage of
        // Postgres ICU collation / locale case-folding (e.g. Turkish-i, accents); that lives in integration.
        Assert.True(names.SequenceEqual(names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)));
        foreach (var name in names)
            Assert.Contains("a", name, StringComparison.OrdinalIgnoreCase);
    }

    private async Task SeedPartiesAsync(params string[] names)
    {
        foreach (var name in names)
            _db.Parties.Add(new Party { Name = name });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
