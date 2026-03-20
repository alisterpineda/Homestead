using Homestead.WebApi.Authentication;
using Homestead.WebApi.Domains.Documents;
using Homestead.WebApi.Features.Documents.v1.CreateTag;
using Homestead.WebApi.Persistence;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Documents;

public class CreateTagHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly ITagPathResolver _tagPathResolver;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditLogger _auditLogger;
    private readonly CreateTagHandler _sut;

    public CreateTagHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _tagPathResolver = Substitute.For<ITagPathResolver>();
        _currentUser = Substitute.For<ICurrentUserAccessor>();
        _currentUser.UserId.Returns(Guid.NewGuid());
        _auditLogger = Substitute.For<IAuditLogger>();
        _sut = new CreateTagHandler(_db, _tagPathResolver, _currentUser, _auditLogger);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task HandleAsync_LogsCreatedAudit()
    {
        var tagId = Guid.NewGuid();
        var tag = new Tag { Id = tagId, Name = "Test", Path = "Test" };

        _tagPathResolver.ResolveOrCreateAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(tag);

        await _sut.HandleAsync(new CreateTagRequest("Test"), CancellationToken.None);

        _auditLogger.Received(1).LogCreated("Tag", tagId, Arg.Any<Guid?>(), Arg.Any<Guid?>());
    }

    [Fact]
    public async Task HandleAsync_PassesDescriptionToResolver()
    {
        var tag = new Tag { Id = Guid.NewGuid(), Name = "Test", Path = "Test", Description = "My desc" };

        _tagPathResolver.ResolveOrCreateAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(tag);

        var result = await _sut.HandleAsync(new CreateTagRequest("Test", Description: "My desc"), CancellationToken.None);

        await _tagPathResolver.Received(1).ResolveOrCreateAsync("Test", leafColor: null, leafDescription: "My desc", createdByUserId: Arg.Any<Guid?>(), ct: Arg.Any<CancellationToken>());
        Assert.Equal("My desc", result.Description);
    }

    [Fact]
    public async Task HandleAsync_WithNullDescription_ReturnsNullDescription()
    {
        var tag = new Tag { Id = Guid.NewGuid(), Name = "Test", Path = "Test" };

        _tagPathResolver.ResolveOrCreateAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(tag);

        var result = await _sut.HandleAsync(new CreateTagRequest("Test"), CancellationToken.None);

        Assert.Null(result.Description);
    }
}
