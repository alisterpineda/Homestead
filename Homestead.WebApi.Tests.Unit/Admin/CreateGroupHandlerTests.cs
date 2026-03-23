using Homestead.WebApi.Authentication;
using Homestead.WebApi.Features.Admin.v1.CreateGroup;
using Homestead.WebApi.Persistence;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Admin;

public class CreateGroupHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditLogger _auditLogger;
    private readonly CreateGroupHandler _sut;

    public CreateGroupHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _currentUser = Substitute.For<ICurrentUserAccessor>();
        _currentUser.UserId.Returns(Guid.NewGuid());
        _auditLogger = Substitute.For<IAuditLogger>();
        _sut = new CreateGroupHandler(_db, _currentUser, _auditLogger);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Handle_LogsCreatedAudit()
    {
        var request = new CreateGroupCommand { Name = "Engineering" };

        var response = await _sut.Handle(request, CancellationToken.None);

        _auditLogger.Received(1).LogCreated("Group", response.Id, Arg.Any<Guid?>());
    }
}
