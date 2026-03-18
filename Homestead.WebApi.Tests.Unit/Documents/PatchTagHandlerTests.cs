using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Domains.Auditing;
using Homestead.WebApi.Domains.Documents;
using Homestead.WebApi.Features.Documents.v1.PatchTag;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson.Operations;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Documents;

public class PatchTagHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditLogger _auditLogger;
    private readonly PatchTagHandler _sut;

    public PatchTagHandlerTests()
    {
        _db = TestAppDbContext.Create();
        var accessService = Substitute.For<IDocumentQueryService>();
        _currentUser = Substitute.For<ICurrentUserAccessor>();
        _currentUser.UserId.Returns(Guid.NewGuid());
        _auditLogger = Substitute.For<IAuditLogger>();
        _sut = new PatchTagHandler(_db, accessService, _currentUser, _auditLogger);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task HandleAsync_WithNameChange_LogsUpdatedAudit()
    {
        var tag = new Tag { Id = Guid.NewGuid(), Name = "Old", Path = "Old" };
        _db.Tags.Add(tag);
        await _db.SaveChangesAsync();

        var patch = new JsonPatchDocument<PatchTagModel>();
        patch.Replace(m => m.Name, "New");

        var command = new PatchTagCommand { Id = tag.Id, PatchDocument = patch };

        await _sut.HandleAsync(command, CancellationToken.None);

        _auditLogger.Received(1).LogUpdated("Tag", tag.Id, Arg.Any<Guid?>(),
            Arg.Is<List<FieldChange>>(c => c.Count > 0), Arg.Any<Guid?>());
    }

    [Fact]
    public async Task HandleAsync_NoChanges_LogsEmptyChanges()
    {
        var tag = new Tag { Id = Guid.NewGuid(), Name = "Same", Path = "Same" };
        _db.Tags.Add(tag);
        await _db.SaveChangesAsync();

        var patch = new JsonPatchDocument<PatchTagModel>();
        patch.Replace(m => m.Name, "Same");

        var command = new PatchTagCommand { Id = tag.Id, PatchDocument = patch };

        await _sut.HandleAsync(command, CancellationToken.None);

        _auditLogger.Received(1).LogUpdated("Tag", tag.Id, Arg.Any<Guid?>(),
            Arg.Is<List<FieldChange>>(c => c.Count == 0), Arg.Any<Guid?>());
    }
}
