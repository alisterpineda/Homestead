using Homestead.WebApi.Authentication;
using Homestead.WebApi.Domains.Auditing;
using Homestead.WebApi.Domains.Groups;
using Homestead.WebApi.Domains.Identity;
using Homestead.WebApi.Features.Admin.v1.AddGroupMember;
using Homestead.WebApi.Persistence;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Admin;

public class AddGroupMemberHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditLogger _auditLogger;
    private readonly AddGroupMemberHandler _sut;

    public AddGroupMemberHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _currentUser = Substitute.For<ICurrentUserAccessor>();
        _currentUser.UserId.Returns(Guid.NewGuid());
        _auditLogger = Substitute.For<IAuditLogger>();
        _sut = new AddGroupMemberHandler(_db, _currentUser, _auditLogger);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task HandleAsync_LogsUpdatedAuditWithMemberAdded()
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
        await _db.SaveChangesAsync();

        var command = new AddGroupMemberCommand
        {
            GroupId = groupId,
            Request = new AddGroupMemberRequest { UserId = userId, AccessLevel = GroupAccessLevel.Editor }
        };

        await _sut.HandleAsync(command, CancellationToken.None);

        _auditLogger.Received(1).LogUpdated("Group", groupId, Arg.Any<Guid?>(),
            Arg.Is<List<FieldChange>>(c => c.Count == 1 && ((ScalarFieldChange)c[0]).Field == "MemberAdded"),
            Arg.Any<Guid?>());
    }
}
