using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Modules.Documents.Features.Parties.CreateParty;
using Homestead.WebApi.Persistence;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Documents;

public class CreatePartyHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;
    private readonly CreatePartyHandler _sut;

    public CreatePartyHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _currentUser = Substitute.For<ICurrentUserAccessor>();
        _currentUser.UserId.Returns(Guid.NewGuid());
        _audit = Substitute.For<IAuditContext>();
        _sut = new CreatePartyHandler(_db, _currentUser, _audit);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Handle_LogsCreatedAudit()
    {
        var request = new CreatePartyCommand { Name = "Acme Corp" };

        var response = await _sut.Handle(request, CancellationToken.None);

        _audit.Received(1).Created("Party", response.Id, Arg.Any<Guid?>());
    }
}
