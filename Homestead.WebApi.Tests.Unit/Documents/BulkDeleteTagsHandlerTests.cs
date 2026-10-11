using Homestead.WebApi.Authorization;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Modules.Documents.Features.Tags.BulkDeleteTags;
using Homestead.WebApi.Persistence;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Documents;

public class BulkDeleteTagsHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IAuditContext _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly BulkDeleteTagsHandler _sut;

    public BulkDeleteTagsHandlerTests()
    {
        _db = TestAppDbContext.Create();
        var accessService = Substitute.For<IDocumentQueryService>();
        _audit = Substitute.For<IAuditContext>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));
        _sut = new BulkDeleteTagsHandler(_db, accessService, _audit, _unitOfWork);
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
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await _sut.HandleAsync(new BulkDeleteTagsCommand([id1, id2]), TestContext.Current.CancellationToken);

        _audit.Received(1).Deleted("Tag", id1, "Tag A", Arg.Any<Guid?>());
        _audit.Received(1).Deleted("Tag", id2, "Tag B", Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_NoTags_DoesNotLogAudit()
    {
        await _sut.HandleAsync(new BulkDeleteTagsCommand([Guid.NewGuid()]), TestContext.Current.CancellationToken);

        _audit.DidNotReceive().Deleted(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<Guid?>());
    }
}
