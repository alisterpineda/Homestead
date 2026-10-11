using FluentValidation;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Groups;
using Homestead.WebApi.Modules.Admin.Features.Groups.UpdateGroup;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Persistence;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Admin;

public class UpdateGroupHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IAuditContext _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly UpdateGroupHandler _sut;

    public UpdateGroupHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _audit = Substitute.For<IAuditContext>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(Guid.NewGuid());
        _sut = new UpdateGroupHandler(_db, _audit, currentUser, _unitOfWork, new UpdateGroupRequestValidator());
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Handle_WithChanges_LogsUpdatedAudit()
    {
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = "Old Name",
            Description = "Old desc",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.Groups.Add(group);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await _sut.HandleAsync(group.Id,
            new UpdateGroupRequest { Name = "New Name", Description = "New desc" }, TestContext.Current.CancellationToken);

        _audit.Received(1).Track<GroupSnapshot, Group>(Arg.Any<Group>(), "Group", group.Id, Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_WithChanges_SetsUpdatedAtAndUpdatedByUserId()
    {
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = "Old Name",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.Groups.Add(group);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var before = DateTimeOffset.UtcNow;

        var result = await _sut.HandleAsync(group.Id,
            new UpdateGroupRequest { Name = "New Name" }, TestContext.Current.CancellationToken);

        Assert.True(result.UpdatedAt >= before);
        Assert.NotNull(result.UpdatedByUserId);
    }

    [Fact]
    public async Task Handle_NoChanges_StillCallsTrack()
    {
        var group = new Group
        {
            Id = Guid.NewGuid(),
            Name = "Same",
            Description = "Same desc",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.Groups.Add(group);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await _sut.HandleAsync(group.Id,
            new UpdateGroupRequest { Name = "Same", Description = "Same desc" }, TestContext.Current.CancellationToken);

        // Track is always called before mutations; diff computation is deferred
        _audit.Received(1).Track<GroupSnapshot, Group>(Arg.Any<Group>(), "Group", group.Id, Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_BlankName_ThrowsValidationException()
    {
        await Assert.ThrowsAsync<ValidationException>(() => _sut.HandleAsync(Guid.NewGuid(),
            new UpdateGroupRequest { Name = "   " }, TestContext.Current.CancellationToken));
    }
}
