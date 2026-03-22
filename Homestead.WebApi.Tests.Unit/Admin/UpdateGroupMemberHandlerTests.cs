using Homestead.WebApi.Domains.Auditing;
using Homestead.WebApi.Domains.Groups;
using Homestead.WebApi.Domains.Identity;
using Homestead.WebApi.Features.Admin.v1.UpdateGroupMember;
using Homestead.WebApi.Persistence;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Admin;

public class UpdateGroupMemberHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IAuditLogger _auditLogger;
    private readonly UpdateGroupMemberHandler _sut;

    public UpdateGroupMemberHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _auditLogger = Substitute.For<IAuditLogger>();
        _sut = new UpdateGroupMemberHandler(_db, _auditLogger);
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
            UserName = "user@test.com",
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
        await _db.SaveChangesAsync();

        var command = new UpdateGroupMemberCommand
        {
            GroupId = groupId,
            UserId = userId,
            Request = new UpdateGroupMemberRequest { AccessLevel = GroupAccessLevel.Editor }
        };

        await _sut.Handle(command, CancellationToken.None);

        _auditLogger.Received(1).LogUpdated("Group", groupId,
            Arg.Is<List<FieldChange>>(c => c.Count == 1 && ((ScalarFieldChange)c[0]).Field == "MemberAccessLevel"),
            Arg.Any<Guid?>());
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

        var command = new UpdateGroupMemberCommand
        {
            GroupId = groupId,
            UserId = userId,
            Request = new UpdateGroupMemberRequest { AccessLevel = GroupAccessLevel.Editor }
        };

        await _sut.Handle(command, CancellationToken.None);

        _auditLogger.DidNotReceive().LogUpdated(Arg.Any<string>(), Arg.Any<Guid>(),
            Arg.Any<List<FieldChange>>(), Arg.Any<Guid?>());
    }
}
