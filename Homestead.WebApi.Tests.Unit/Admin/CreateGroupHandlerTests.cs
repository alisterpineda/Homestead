using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Admin.Features.Groups.CreateGroup;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Persistence;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Admin;

public class CreateGroupHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly CreateGroupHandler _sut;

    public CreateGroupHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _currentUser = Substitute.For<ICurrentUserAccessor>();
        _currentUser.UserId.Returns(Guid.NewGuid());
        _audit = Substitute.For<IAuditContext>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));
        _sut = new CreateGroupHandler(_db, _currentUser, _audit, _unitOfWork);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Handle_LogsCreatedAudit()
    {
        var request = new CreateGroupCommand { Name = "Engineering" };

        var response = await _sut.Handle(request, CancellationToken.None);

        _audit.Received(1).Created("Group", response.Id, Arg.Any<Guid?>());
    }
}
