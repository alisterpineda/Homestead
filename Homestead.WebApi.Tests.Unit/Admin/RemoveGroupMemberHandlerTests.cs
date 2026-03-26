using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Groups;
using Homestead.WebApi.Domain.Identity;
using Homestead.WebApi.Features.Admin.Groups.RemoveGroupMember;
using Homestead.WebApi.Persistence;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Admin;

public class RemoveGroupMemberHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IAuditLogger _auditLogger;
    private readonly RemoveGroupMemberHandler _sut;

    public RemoveGroupMemberHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _auditLogger = Substitute.For<IAuditLogger>();
        _sut = new RemoveGroupMemberHandler(_db, _auditLogger);
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
            UserName = "user@test.com",
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
        await _db.SaveChangesAsync();

        var command = new RemoveGroupMemberCommand { GroupId = groupId, UserId = userId };

        await _sut.Handle(command, CancellationToken.None);

        _auditLogger.Received(1).LogUpdated("Group", groupId,
            Arg.Is<List<FieldChange>>(c => c.Count == 1 && ((ScalarFieldChange)c[0]).Field == "MemberRemoved"),
            Arg.Any<Guid?>());
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
            UserName = "owner@test.com",
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
        await _db.SaveChangesAsync();

        var command = new RemoveGroupMemberCommand { GroupId = groupId, UserId = userId };

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.Handle(command, CancellationToken.None));
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
            UserName = "owner1@test.com",
            Email = "owner1@test.com",
            DisplayName = "Owner One",
            IsActive = true
        });
        _db.Users.Add(new AppUser
        {
            Id = userId2,
            UserName = "owner2@test.com",
            Email = "owner2@test.com",
            DisplayName = "Owner Two",
            IsActive = true
        });
        _db.GroupMembers.Add(new GroupMember { GroupId = groupId, UserId = userId1, AccessLevel = GroupAccessLevel.Owner });
        _db.GroupMembers.Add(new GroupMember { GroupId = groupId, UserId = userId2, AccessLevel = GroupAccessLevel.Owner });
        await _db.SaveChangesAsync();

        var command = new RemoveGroupMemberCommand { GroupId = groupId, UserId = userId1 };

        await _sut.Handle(command, CancellationToken.None);

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
            UserName = "owner@test.com",
            Email = "owner@test.com",
            DisplayName = "Owner User",
            IsActive = true
        });
        _db.Users.Add(new AppUser
        {
            Id = editorId,
            UserName = "editor@test.com",
            Email = "editor@test.com",
            DisplayName = "Editor User",
            IsActive = true
        });
        _db.GroupMembers.Add(new GroupMember { GroupId = groupId, UserId = ownerId, AccessLevel = GroupAccessLevel.Owner });
        _db.GroupMembers.Add(new GroupMember { GroupId = groupId, UserId = editorId, AccessLevel = GroupAccessLevel.Editor });
        await _db.SaveChangesAsync();

        var command = new RemoveGroupMemberCommand { GroupId = groupId, UserId = editorId };

        await _sut.Handle(command, CancellationToken.None);

        Assert.Single(_db.GroupMembers.Where(gm => gm.GroupId == groupId));
        Assert.True(_db.GroupMembers.Any(gm => gm.GroupId == groupId && gm.UserId == ownerId));
    }
}
