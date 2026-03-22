using Homestead.WebApi.Authorization;
using Homestead.WebApi.Domains.Parties;
using Homestead.WebApi.Features.Documents.v1.BulkDeleteParties;
using Homestead.WebApi.Persistence;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Documents;

public class BulkDeletePartiesHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IAuditLogger _auditLogger;
    private readonly BulkDeletePartiesHandler _sut;

    public BulkDeletePartiesHandlerTests()
    {
        _db = TestAppDbContext.Create();
        var accessService = Substitute.For<IDocumentQueryService>();
        _auditLogger = Substitute.For<IAuditLogger>();
        _sut = new BulkDeletePartiesHandler(_db, accessService, _auditLogger);
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
        await _db.SaveChangesAsync();

        await _sut.Handle(new BulkDeletePartiesRequest([id1, id2]), CancellationToken.None);

        _auditLogger.Received(1).LogDeleted("Party", id1, "Party A", Arg.Any<Guid?>());
        _auditLogger.Received(1).LogDeleted("Party", id2, "Party B", Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_NoParties_DoesNotLogAudit()
    {
        await _sut.Handle(new BulkDeletePartiesRequest([Guid.NewGuid()]), CancellationToken.None);

        _auditLogger.DidNotReceive().LogDeleted(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<Guid?>());
    }
}
