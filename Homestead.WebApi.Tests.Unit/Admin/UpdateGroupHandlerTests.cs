using Homestead.WebApi.Authentication;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Groups;
using Homestead.WebApi.Features.Admin.Groups.UpdateGroup;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Persistence;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Admin;

public class UpdateGroupHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IAuditContext _audit;
    private readonly UpdateGroupHandler _sut;

    public UpdateGroupHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _audit = Substitute.For<IAuditContext>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(Guid.NewGuid());
        _sut = new UpdateGroupHandler(_db, _audit, currentUser);
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
        await _db.SaveChangesAsync();

        var command = new UpdateGroupCommand
        {
            Id = group.Id,
            Request = new UpdateGroupRequest { Name = "New Name", Description = "New desc" }
        };

        await _sut.Handle(command, CancellationToken.None);

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
        await _db.SaveChangesAsync();

        var before = DateTimeOffset.UtcNow;

        var command = new UpdateGroupCommand
        {
            Id = group.Id,
            Request = new UpdateGroupRequest { Name = "New Name" }
        };

        var result = await _sut.Handle(command, CancellationToken.None);

        Assert.NotNull(result.UpdatedAt);
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
        await _db.SaveChangesAsync();

        var command = new UpdateGroupCommand
        {
            Id = group.Id,
            Request = new UpdateGroupRequest { Name = "Same", Description = "Same desc" }
        };

        await _sut.Handle(command, CancellationToken.None);

        // Track is always called before mutations; diff computation is deferred
        _audit.Received(1).Track<GroupSnapshot, Group>(Arg.Any<Group>(), "Group", group.Id, Arg.Any<Guid?>());
    }
}
