using FluentValidation;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Modules.Documents.Features.Comments.CreateComment;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Documents;

public class CreateCommentHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly CreateCommentHandler _sut;

    public CreateCommentHandlerTests()
    {
        _db = TestAppDbContext.Create();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(Guid.NewGuid());
        _sut = new CreateCommentHandler(_db, currentUser, Substitute.For<IAuditContext>(),
            Substitute.For<IUnitOfWork>(), new CreateCommentCommandValidator());
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Handle_EmptyBody_ThrowsValidationException()
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => _sut.HandleAsync(new CreateCommentCommand(""), TestContext.Current.CancellationToken));
    }
}
