using FluentValidation;
using Homestead.WebApi.Domain.Parties;
using Homestead.WebApi.Modules.Documents.Features.Parties.GetPartiesByIds;

namespace Homestead.WebApi.Tests.Unit.Documents;

public class GetPartiesByIdsHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly GetPartiesByIdsHandler _sut;

    public GetPartiesByIdsHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _sut = new GetPartiesByIdsHandler(_db, new GetPartiesByIdsQueryValidator());
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Handle_KnownIds_ReturnsMatchingParties()
    {
        var ct = TestContext.Current.CancellationToken;
        var partyA = new Party { Name = "Alpha" };
        var partyB = new Party { Name = "Bravo" };
        var partyC = new Party { Name = "Charlie" };
        _db.Parties.AddRange(partyA, partyB, partyC);
        await _db.SaveChangesAsync(ct);

        var result = await _sut.HandleAsync(
            new GetPartiesByIdsQuery(new[] { partyA.Id, partyB.Id }),
            ct);

        Assert.Equal(2, result.Count);
        var byId = result.ToDictionary(p => p.Id, p => p.Name);
        Assert.Equal("Alpha", byId[partyA.Id]);
        Assert.Equal("Bravo", byId[partyB.Id]);
    }

    [Fact]
    public async Task Handle_UnknownIds_AreSilentlyOmitted()
    {
        var ct = TestContext.Current.CancellationToken;
        var partyA = new Party { Name = "Alpha" };
        _db.Parties.Add(partyA);
        await _db.SaveChangesAsync(ct);

        var unknownId = Guid.NewGuid();
        var result = await _sut.HandleAsync(
            new GetPartiesByIdsQuery(new[] { partyA.Id, unknownId }),
            ct);

        Assert.Single(result);
        Assert.Equal(partyA.Id, result[0].Id);
    }

    [Fact]
    public async Task Handle_AllUnknownIds_ReturnsEmptyList()
    {
        var ct = TestContext.Current.CancellationToken;
        _db.Parties.Add(new Party { Name = "Alpha" });
        await _db.SaveChangesAsync(ct);

        var result = await _sut.HandleAsync(
            new GetPartiesByIdsQuery(new[] { Guid.NewGuid(), Guid.NewGuid() }),
            ct);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Handle_EmptyIdList_ThrowsValidationException()
    {
        await Assert.ThrowsAsync<ValidationException>(() => _sut.HandleAsync(
            new GetPartiesByIdsQuery(Array.Empty<Guid>()),
            TestContext.Current.CancellationToken));
    }
}
