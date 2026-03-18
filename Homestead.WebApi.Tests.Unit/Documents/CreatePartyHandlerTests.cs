using Homestead.WebApi.Authentication;
using Homestead.WebApi.Features.Documents.v1.CreateParty;
using Homestead.WebApi.Persistence;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Documents;

public class CreatePartyHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditLogger _auditLogger;
    private readonly CreatePartyHandler _sut;

    public CreatePartyHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _currentUser = Substitute.For<ICurrentUserAccessor>();
        _currentUser.UserId.Returns(Guid.NewGuid());
        _auditLogger = Substitute.For<IAuditLogger>();
        _sut = new CreatePartyHandler(_db, _currentUser, _auditLogger);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task HandleAsync_LogsCreatedAudit()
    {
        var request = new CreatePartyRequest { Name = "Acme Corp" };

        var response = await _sut.HandleAsync(request, CancellationToken.None);

        _auditLogger.Received(1).LogCreated("Party", response.Id, Arg.Any<Guid?>(), Arg.Any<Guid?>());
    }
}
