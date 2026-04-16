using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Groups;
using Homestead.WebApi.Domain.Identity;
using Homestead.WebApi.Modules.Admin.Features.Groups.AddGroupMember;
using Homestead.WebApi.Persistence;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Admin;

public class AddGroupMemberHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IAuditLogger _auditLogger;
    private readonly AddGroupMemberHandler _sut;

    public AddGroupMemberHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _auditLogger = Substitute.For<IAuditLogger>();
        _sut = new AddGroupMemberHandler(_db, _auditLogger);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Handle_LogsUpdatedAuditWithMemberAdded()
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
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = new AddGroupMemberCommand
        {
            GroupId = groupId,
            Request = new AddGroupMemberRequest { UserId = userId, AccessLevel = GroupAccessLevel.Editor }
        };

        await _sut.Handle(command, TestContext.Current.CancellationToken);

        _auditLogger.Received(1).LogUpdated("Group", groupId,
            Arg.Is<List<FieldChange>>(c => c.Count == 1 && ((ScalarFieldChange)c[0]).Field == "MemberAdded"),
            Arg.Any<Guid?>());
    }
}
