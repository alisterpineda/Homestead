using FluentValidation;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Groups;
using Homestead.WebApi.Domain.Identity;
using Homestead.WebApi.Modules.Admin.Features.Groups.UpdateGroupMember;
using Homestead.WebApi.Persistence;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Admin;

public class UpdateGroupMemberHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IAuditContext _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly UpdateGroupMemberHandler _sut;

    public UpdateGroupMemberHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _audit = Substitute.For<IAuditContext>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));
        _sut = new UpdateGroupMemberHandler(_db, _audit, _unitOfWork, new UpdateGroupMemberCommandValidator());
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Handle_AccessLevelChanged_LogsUpdatedAudit()
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
            AccessLevel = GroupAccessLevel.Viewer
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new UpdateGroupMemberCommand
        {
            GroupId = groupId,
            UserId = userId,
            Request = new UpdateGroupMemberRequest { AccessLevel = GroupAccessLevel.Editor }
        };

        await _sut.Handle(command, TestContext.Current.CancellationToken);

        _audit.Received(1).TrackScalar("Group", groupId, "MemberAccessLevel",
            Arg.Any<string?>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task Handle_AccessLevelUnchanged_DoesNotLogAudit()
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

        var command = new UpdateGroupMemberCommand
        {
            GroupId = groupId,
            UserId = userId,
            Request = new UpdateGroupMemberRequest { AccessLevel = GroupAccessLevel.Editor }
        };

        await _sut.Handle(command, TestContext.Current.CancellationToken);

        _audit.DidNotReceive().TrackScalar(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_DowngradeLastOwner_ThrowsBusinessRuleException()
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

        var command = new UpdateGroupMemberCommand
        {
            GroupId = groupId,
            UserId = userId,
            Request = new UpdateGroupMemberRequest { AccessLevel = GroupAccessLevel.Editor }
        };

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.Handle(command, TestContext.Current.CancellationToken));
        Assert.Contains("last owner", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handle_DowngradeOwner_WhenOtherOwnersExist_Succeeds()
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

        var command = new UpdateGroupMemberCommand
        {
            GroupId = groupId,
            UserId = userId1,
            Request = new UpdateGroupMemberRequest { AccessLevel = GroupAccessLevel.Editor }
        };

        var result = await _sut.Handle(command, TestContext.Current.CancellationToken);

        Assert.Equal(GroupAccessLevel.Editor, result.AccessLevel);
    }

    [Fact]
    public async Task Handle_OwnerToOwner_NoOp_Succeeds()
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

        var command = new UpdateGroupMemberCommand
        {
            GroupId = groupId,
            UserId = userId,
            Request = new UpdateGroupMemberRequest { AccessLevel = GroupAccessLevel.Owner }
        };

        var result = await _sut.Handle(command, TestContext.Current.CancellationToken);

        Assert.Equal(GroupAccessLevel.Owner, result.AccessLevel);
    }

    [Fact]
    public async Task Handle_UndefinedAccessLevel_ThrowsValidationException()
    {
        var command = new UpdateGroupMemberCommand
        {
            GroupId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Request = new UpdateGroupMemberRequest { AccessLevel = (GroupAccessLevel)42 }
        };

        await Assert.ThrowsAsync<ValidationException>(() => _sut.Handle(command, TestContext.Current.CancellationToken));
    }
}
