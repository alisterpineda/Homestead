using Homestead.WebApi.Authorization;
using Homestead.WebApi.Domains.Documents;
using Homestead.WebApi.Features.Documents.v1.BulkDeleteTags;
using Homestead.WebApi.Persistence;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Documents;

public class BulkDeleteTagsHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IAuditLogger _auditLogger;
    private readonly BulkDeleteTagsHandler _sut;

    public BulkDeleteTagsHandlerTests()
    {
        _db = TestAppDbContext.Create();
        var accessService = Substitute.For<IDocumentQueryService>();
        _auditLogger = Substitute.For<IAuditLogger>();
        _sut = new BulkDeleteTagsHandler(_db, accessService, _auditLogger);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Handle_LogsDeletedAuditPerTag()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        _db.Tags.AddRange(
            new Tag { Id = id1, Name = "Tag A", Path = "Tag A" },
            new Tag { Id = id2, Name = "Tag B", Path = "Tag B" }
        );
        await _db.SaveChangesAsync();

        await _sut.Handle(new BulkDeleteTagsCommand([id1, id2]), CancellationToken.None);

        _auditLogger.Received(1).LogDeleted("Tag", id1, "Tag A", Arg.Any<Guid?>());
        _auditLogger.Received(1).LogDeleted("Tag", id2, "Tag B", Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_NoTags_DoesNotLogAudit()
    {
        await _sut.Handle(new BulkDeleteTagsCommand([Guid.NewGuid()]), CancellationToken.None);

        _auditLogger.DidNotReceive().LogDeleted(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<Guid?>());
    }
}
