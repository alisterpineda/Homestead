using FluentValidation;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Groups;
using Homestead.WebApi.Domain.Identity;
using Homestead.WebApi.Modules.Admin.Features.Groups.AddGroupMember;
using Homestead.WebApi.Persistence;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Admin;

public class AddGroupMemberHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IAuditContext _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly AddGroupMemberHandler _sut;

    public AddGroupMemberHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _audit = Substitute.For<IAuditContext>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));
        _sut = new AddGroupMemberHandler(_db, _audit, _unitOfWork, new AddGroupMemberCommandValidator());
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

        _audit.Received(1).TrackScalar("Group", groupId, "MemberAdded",
            Arg.Any<string?>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task Handle_EmptyUserId_ThrowsValidationException()
    {
        var command = new AddGroupMemberCommand
        {
            GroupId = Guid.NewGuid(),
            Request = new AddGroupMemberRequest { UserId = Guid.Empty, AccessLevel = GroupAccessLevel.Editor }
        };

        await Assert.ThrowsAsync<ValidationException>(() => _sut.Handle(command, TestContext.Current.CancellationToken));
    }
}
