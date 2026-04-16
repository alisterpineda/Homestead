using Homestead.WebApi.Authorization;
using Homestead.WebApi.Domain.Parties;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Modules.Documents.Features.Parties.BulkDeleteParties;
using Homestead.WebApi.Persistence;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Documents;

public class BulkDeletePartiesHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IAuditContext _audit;
    private readonly BulkDeletePartiesHandler _sut;

    public BulkDeletePartiesHandlerTests()
    {
        _db = TestAppDbContext.Create();
        var accessService = Substitute.For<IDocumentQueryService>();
        _audit = Substitute.For<IAuditContext>();
        _sut = new BulkDeletePartiesHandler(_db, accessService, _audit);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Handle_LogsDeletedAuditPerParty()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        _db.Parties.AddRange(
            new Party { Id = id1, Name = "Party A", CreatedAt = DateTimeOffset.UtcNow },
            new Party { Id = id2, Name = "Party B", CreatedAt = DateTimeOffset.UtcNow }
        );
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await _sut.Handle(new BulkDeletePartiesCommand([id1, id2]), TestContext.Current.CancellationToken);

        _audit.Received(1).Deleted("Party", id1, "Party A", Arg.Any<Guid?>());
        _audit.Received(1).Deleted("Party", id2, "Party B", Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_NoParties_DoesNotLogAudit()
    {
        await _sut.Handle(new BulkDeletePartiesCommand([Guid.NewGuid()]), TestContext.Current.CancellationToken);

        _audit.DidNotReceive().Deleted(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<Guid?>());
    }
}
