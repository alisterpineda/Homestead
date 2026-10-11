using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Groups;
using Homestead.WebApi.Domain.Identity;
using Homestead.WebApi.Modules.Admin.Features.Groups.RemoveGroupMember;
using Homestead.WebApi.Persistence;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Admin;

public class RemoveGroupMemberHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IAuditContext _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly RemoveGroupMemberHandler _sut;

    public RemoveGroupMemberHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _audit = Substitute.For<IAuditContext>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));
        _sut = new RemoveGroupMemberHandler(_db, _audit, _unitOfWork);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Handle_LogsUpdatedAuditWithMemberRemoved()
    {
        var groupId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        _db.Groups.Add(new Group { Id = groupId, Name = "Test Group", CreatedAt = DateTimeOffset.UtcNow });
        _db.Users.Add(new AppUser
        {
            Id = userId,
            Email = "user@test.com",
            DisplayName = "Test User",
            IsActive = true
        });
        _db.GroupMembers.Add(new GroupMember
        {
            GroupId = groupId,
            UserId = userId,
            AccessLevel = GroupAccessLevel.Editor
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await _sut.HandleAsync(groupId, userId, TestContext.Current.CancellationToken);

        _audit.Received(1).TrackScalar("Group", groupId, "MemberRemoved",
            Arg.Any<string?>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task Handle_RemoveLastOwner_ThrowsBusinessRuleException()
    {
        var groupId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        _db.Groups.Add(new Group { Id = groupId, Name = "Test Group", CreatedAt = DateTimeOffset.UtcNow });
        _db.Users.Add(new AppUser
        {
            Id = userId,
            Email = "owner@test.com",
            DisplayName = "Owner User",
            IsActive = true
        });
        _db.GroupMembers.Add(new GroupMember
        {
            GroupId = groupId,
            UserId = userId,
            AccessLevel = GroupAccessLevel.Owner
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.HandleAsync(groupId, userId, TestContext.Current.CancellationToken));
        Assert.Contains("last owner", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_RemoveOwner_WhenOtherOwnersExist_Succeeds()
    {
        var groupId = Guid.NewGuid();
        var userId1 = Guid.NewGuid();
        var userId2 = Guid.NewGuid();

        _db.Groups.Add(new Group { Id = groupId, Name = "Test Group", CreatedAt = DateTimeOffset.UtcNow });
        _db.Users.Add(new AppUser
        {
            Id = userId1,
            Email = "owner1@test.com",
            DisplayName = "Owner One",
            IsActive = true
        });
        _db.Users.Add(new AppUser
        {
            Id = userId2,
            Email = "owner2@test.com",
            DisplayName = "Owner Two",
            IsActive = true
        });
        _db.GroupMembers.Add(new GroupMember { GroupId = groupId, UserId = userId1, AccessLevel = GroupAccessLevel.Owner });
        _db.GroupMembers.Add(new GroupMember { GroupId = groupId, UserId = userId2, AccessLevel = GroupAccessLevel.Owner });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await _sut.HandleAsync(groupId, userId1, TestContext.Current.CancellationToken);

        Assert.Single(_db.GroupMembers.Where(gm => gm.GroupId == groupId));
    }

    [Fact]
    public async Task Handle_RemoveNonOwner_Succeeds()
    {
        var groupId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var editorId = Guid.NewGuid();

        _db.Groups.Add(new Group { Id = groupId, Name = "Test Group", CreatedAt = DateTimeOffset.UtcNow });
        _db.Users.Add(new AppUser
        {
            Id = ownerId,
            Email = "owner@test.com",
            DisplayName = "Owner User",
            IsActive = true
        });
        _db.Users.Add(new AppUser
        {
            Id = editorId,
            Email = "editor@test.com",
            DisplayName = "Editor User",
            IsActive = true
        });
        _db.GroupMembers.Add(new GroupMember { GroupId = groupId, UserId = ownerId, AccessLevel = GroupAccessLevel.Owner });
        _db.GroupMembers.Add(new GroupMember { GroupId = groupId, UserId = editorId, AccessLevel = GroupAccessLevel.Editor });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await _sut.HandleAsync(groupId, editorId, TestContext.Current.CancellationToken);

        Assert.Single(_db.GroupMembers.Where(gm => gm.GroupId == groupId));
        Assert.True(_db.GroupMembers.Any(gm => gm.GroupId == groupId && gm.UserId == ownerId));
    }
}
