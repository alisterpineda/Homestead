using Homestead.WebApi.Features.Documents;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Features.Documents.Tags.CreateTag;
using Homestead.WebApi.Persistence;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Documents;

public class CreateTagHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly ITagPathResolver _tagPathResolver;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;
    private readonly CreateTagHandler _sut;

    public CreateTagHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _tagPathResolver = Substitute.For<ITagPathResolver>();
        _currentUser = Substitute.For<ICurrentUserAccessor>();
        _currentUser.UserId.Returns(Guid.NewGuid());
        _audit = Substitute.For<IAuditContext>();
        _sut = new CreateTagHandler(_db, _tagPathResolver, _currentUser, _audit);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Handle_LogsCreatedAudit()
    {
        var tagId = Guid.NewGuid();
        var tag = new Tag { Id = tagId, Name = "Test", Path = "Test" };

        _tagPathResolver.ResolveOrCreateAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(tag);

        await _sut.Handle(new CreateTagCommand("Test"), CancellationToken.None);

        _audit.Received(1).Created("Tag", tagId, Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_PassesDescriptionToResolver()
    {
        var tag = new Tag { Id = Guid.NewGuid(), Name = "Test", Path = "Test", Description = "My desc" };

        _tagPathResolver.ResolveOrCreateAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(tag);

        var result = await _sut.Handle(new CreateTagCommand("Test", Description: "My desc"), CancellationToken.None);

        await _tagPathResolver.Received(1).ResolveOrCreateAsync("Test", leafColor: null, leafDescription: "My desc", createdByUserId: Arg.Any<Guid?>(), ct: Arg.Any<CancellationToken>());
        Assert.Equal("My desc", result.Description);
    }

    [Fact]
    public async Task Handle_WithNullDescription_ReturnsNullDescription()
    {
        var tag = new Tag { Id = Guid.NewGuid(), Name = "Test", Path = "Test" };

        _tagPathResolver.ResolveOrCreateAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(tag);

        var result = await _sut.Handle(new CreateTagCommand("Test"), CancellationToken.None);

        Assert.Null(result.Description);
    }
}
