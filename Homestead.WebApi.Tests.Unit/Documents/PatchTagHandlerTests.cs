using Homestead.WebApi.Authorization;
using Homestead.WebApi.Domains.Auditing.Snapshots;
using Homestead.WebApi.Domains.Documents;
using Homestead.WebApi.Features.Auditing;
using Homestead.WebApi.Features.Documents.v1.PatchTag;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson.Operations;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Documents;

public class PatchTagHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IAuditContext _audit;
    private readonly PatchTagHandler _sut;

    public PatchTagHandlerTests()
    {
        _db = TestAppDbContext.Create();
        var accessService = Substitute.For<IDocumentQueryService>();
        _audit = Substitute.For<IAuditContext>();
        _sut = new PatchTagHandler(_db, accessService, _audit);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Handle_WithNameChange_LogsUpdatedAudit()
    {
        var tag = new Tag { Id = Guid.NewGuid(), Name = "Old", Path = "Old" };
        _db.Tags.Add(tag);
        await _db.SaveChangesAsync();

        var patch = new JsonPatchDocument<PatchTagModel>();
        patch.Replace(m => m.Name, "New");

        var command = new PatchTagCommand { Id = tag.Id, PatchDocument = patch };

        await _sut.Handle(command, CancellationToken.None);

        _audit.Received(1).Track<TagSnapshot, Tag>(Arg.Any<Tag>(), "Tag", tag.Id, Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_NoChanges_StillCallsTrack()
    {
        var tag = new Tag { Id = Guid.NewGuid(), Name = "Same", Path = "Same" };
        _db.Tags.Add(tag);
        await _db.SaveChangesAsync();

        var patch = new JsonPatchDocument<PatchTagModel>();
        patch.Replace(m => m.Name, "Same");

        var command = new PatchTagCommand { Id = tag.Id, PatchDocument = patch };

        await _sut.Handle(command, CancellationToken.None);

        // Track is always called before mutations; diff computation is deferred
        _audit.Received(1).Track<TagSnapshot, Tag>(Arg.Any<Tag>(), "Tag", tag.Id, Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_WithDescriptionChange_UpdatesDescription()
    {
        var tag = new Tag { Id = Guid.NewGuid(), Name = "Test", Path = "Test", Description = "Old" };
        _db.Tags.Add(tag);
        await _db.SaveChangesAsync();

        var patch = new JsonPatchDocument<PatchTagModel>();
        patch.Replace(m => m.Description, "New description");

        var command = new PatchTagCommand { Id = tag.Id, PatchDocument = patch };

        var result = await _sut.Handle(command, CancellationToken.None);

        Assert.Equal("New description", result.Description);
        _audit.Received(1).Track<TagSnapshot, Tag>(Arg.Any<Tag>(), "Tag", tag.Id, Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_ClearDescription_SetsNull()
    {
        var tag = new Tag { Id = Guid.NewGuid(), Name = "Test", Path = "Test", Description = "Existing" };
        _db.Tags.Add(tag);
        await _db.SaveChangesAsync();

        var patch = new JsonPatchDocument<PatchTagModel>();
        patch.Replace(m => m.Description, null);

        var command = new PatchTagCommand { Id = tag.Id, PatchDocument = patch };

        var result = await _sut.Handle(command, CancellationToken.None);

        Assert.Null(result.Description);
    }
}
