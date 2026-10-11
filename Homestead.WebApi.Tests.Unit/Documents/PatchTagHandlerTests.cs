using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Modules.Documents.Features.Tags.PatchTag;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson.Operations;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Documents;

public class PatchTagHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IAuditContext _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly PatchTagHandler _sut;

    public PatchTagHandlerTests()
    {
        _db = TestAppDbContext.Create();
        var accessService = Substitute.For<IDocumentQueryService>();
        _audit = Substitute.For<IAuditContext>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(Guid.NewGuid());
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));
        _sut = new PatchTagHandler(_db, accessService, _audit, currentUser, _unitOfWork);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Handle_WithNameChange_LogsUpdatedAudit()
    {
        var tag = new Tag { Id = Guid.NewGuid(), Name = "Old", Path = "Old" };
        _db.Tags.Add(tag);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var patch = new JsonPatchDocument<PatchTagModel>();
        patch.Replace(m => m.Name, "New");

        await _sut.HandleAsync(tag.Id, patch, TestContext.Current.CancellationToken);

        _audit.Received(1).Track<TagSnapshot, Tag>(Arg.Any<Tag>(), "Tag", tag.Id, Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_NoChanges_StillCallsTrack()
    {
        var tag = new Tag { Id = Guid.NewGuid(), Name = "Same", Path = "Same" };
        _db.Tags.Add(tag);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var patch = new JsonPatchDocument<PatchTagModel>();
        patch.Replace(m => m.Name, "Same");

        await _sut.HandleAsync(tag.Id, patch, TestContext.Current.CancellationToken);

        // Track is always called before mutations; diff computation is deferred
        _audit.Received(1).Track<TagSnapshot, Tag>(Arg.Any<Tag>(), "Tag", tag.Id, Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_WithDescriptionChange_UpdatesDescription()
    {
        var tag = new Tag { Id = Guid.NewGuid(), Name = "Test", Path = "Test", Description = "Old" };
        _db.Tags.Add(tag);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var patch = new JsonPatchDocument<PatchTagModel>();
        patch.Replace(m => m.Description, "New description");

        var result = await _sut.HandleAsync(tag.Id, patch, TestContext.Current.CancellationToken);

        Assert.Equal("New description", result.Description);
        _audit.Received(1).Track<TagSnapshot, Tag>(Arg.Any<Tag>(), "Tag", tag.Id, Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_WithNameChange_SetsUpdatedAtAndUpdatedByUserId()
    {
        var tag = new Tag { Id = Guid.NewGuid(), Name = "Old", Path = "Old" };
        _db.Tags.Add(tag);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var before = DateTimeOffset.UtcNow;

        var patch = new JsonPatchDocument<PatchTagModel>();
        patch.Replace(m => m.Name, "New");

        var result = await _sut.HandleAsync(tag.Id, patch, TestContext.Current.CancellationToken);

        Assert.NotNull(result.UpdatedAt);
        Assert.True(result.UpdatedAt >= before);
        Assert.NotNull(result.UpdatedByUserId);
    }

    [Fact]
    public async Task Handle_ClearDescription_SetsNull()
    {
        var tag = new Tag { Id = Guid.NewGuid(), Name = "Test", Path = "Test", Description = "Existing" };
        _db.Tags.Add(tag);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var patch = new JsonPatchDocument<PatchTagModel>();
        patch.Replace(m => m.Description, null);

        var result = await _sut.HandleAsync(tag.Id, patch, TestContext.Current.CancellationToken);

        Assert.Null(result.Description);
    }
}
