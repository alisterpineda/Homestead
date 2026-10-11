using FluentValidation;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
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
    private readonly IUnitOfWork _unitOfWork;
    private readonly CreatePartyHandler _sut;

    public CreatePartyHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _currentUser = Substitute.For<ICurrentUserAccessor>();
        _currentUser.UserId.Returns(Guid.NewGuid());
        _audit = Substitute.For<IAuditContext>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));
        _sut = new CreatePartyHandler(_db, _currentUser, _audit, _unitOfWork, new CreatePartyCommandValidator());
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Handle_LogsCreatedAudit()
    {
        var request = new CreatePartyCommand { Name = "Acme Corp" };

        var response = await _sut.HandleAsync(request, CancellationToken.None);

        _audit.Received(1).Created("Party", response.Id, Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_BlankName_ThrowsValidationException()
    {
        var request = new CreatePartyCommand { Name = "   " };

        await Assert.ThrowsAsync<ValidationException>(
            () => _sut.HandleAsync(request, TestContext.Current.CancellationToken));
    }
}
