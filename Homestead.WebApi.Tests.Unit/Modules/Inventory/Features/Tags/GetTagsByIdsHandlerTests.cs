using FluentValidation;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Tags.GetTagsByIds;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Features.Tags;

public class GetTagsByIdsHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly GetTagsByIdsHandler _sut;

    public GetTagsByIdsHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _sut = new GetTagsByIdsHandler(_db, new GetInventoryTagsByIdsQueryValidator());
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Handle_KnownIds_ReturnsMatchingTags()
    {
        var ct = TestContext.Current.CancellationToken;
        var tagA = new Tag { Name = "Alpha", Path = "Alpha" };
        var tagB = new Tag { Name = "Bravo", Path = "Parent/Bravo" };
        var tagC = new Tag { Name = "Charlie", Path = "Charlie" };
        _db.InventoryTags.AddRange(tagA, tagB, tagC);
        await _db.SaveChangesAsync(ct);

        var result = await _sut.Handle(
            new GetInventoryTagsByIdsQuery(new[] { tagA.Id, tagB.Id }),
            ct);

        Assert.Equal(2, result.Count);
        var byId = result.ToDictionary(t => t.Id, t => t.Path);
        Assert.Equal("Alpha", byId[tagA.Id]);
        Assert.Equal("Parent/Bravo", byId[tagB.Id]);
    }

    [Fact]
    public async Task Handle_UnknownIds_AreSilentlyOmitted()
    {
        var ct = TestContext.Current.CancellationToken;
        var tagA = new Tag { Name = "Alpha", Path = "Alpha" };
        _db.InventoryTags.Add(tagA);
        await _db.SaveChangesAsync(ct);

        var unknownId = Guid.NewGuid();
        var result = await _sut.Handle(
            new GetInventoryTagsByIdsQuery(new[] { tagA.Id, unknownId }),
            ct);

        Assert.Single(result);
        Assert.Equal(tagA.Id, result[0].Id);
    }

    [Fact]
    public async Task Handle_AllUnknownIds_ReturnsEmptyList()
    {
        var ct = TestContext.Current.CancellationToken;
        _db.InventoryTags.Add(new Tag { Name = "Alpha", Path = "Alpha" });
        await _db.SaveChangesAsync(ct);

        var result = await _sut.Handle(
            new GetInventoryTagsByIdsQuery(new[] { Guid.NewGuid(), Guid.NewGuid() }),
            ct);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Handle_EmptyIdList_ThrowsValidationException()
    {
        await Assert.ThrowsAsync<ValidationException>(() => _sut.Handle(
            new GetInventoryTagsByIdsQuery(Array.Empty<Guid>()),
            TestContext.Current.CancellationToken));
    }
}
