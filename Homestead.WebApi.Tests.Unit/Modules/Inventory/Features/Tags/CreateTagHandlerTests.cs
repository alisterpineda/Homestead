using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Tags.CreateTag;
using Homestead.WebApi.Modules.Inventory.Services;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Features.Tags;

public class CreateTagHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IInventoryTagPathResolver _tagPathResolver;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly CreateTagHandler _sut;

    public CreateTagHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _tagPathResolver = Substitute.For<IInventoryTagPathResolver>();
        _currentUser = Substitute.For<ICurrentUserAccessor>();
        _currentUser.UserId.Returns(Guid.NewGuid());
        _audit = Substitute.For<IAuditContext>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));
        _sut = new CreateTagHandler(_tagPathResolver, _currentUser, _audit, _unitOfWork);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Handle_LogsCreatedAudit()
    {
        var tagId = Guid.NewGuid();
        var tag = new Tag { Id = tagId, Name = "Test", Path = "Test" };

        _tagPathResolver.ResolveOrCreateAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(tag);

        await _sut.HandleAsync(new CreateInventoryTagCommand("Test"), CancellationToken.None);

        _audit.Received(1).Created("InventoryTag", tagId, Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_PassesPathColorAndDescriptionToResolver()
    {
        var tag = new Tag { Id = Guid.NewGuid(), Name = "Test", Path = "Parent/Test", Color = "#ff0000", Description = "My desc" };

        _tagPathResolver.ResolveOrCreateAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(tag);

        var result = await _sut.HandleAsync(new CreateInventoryTagCommand("Parent/Test", Color: "#ff0000", Description: "My desc"), CancellationToken.None);

        await _tagPathResolver.Received(1).ResolveOrCreateAsync("Parent/Test", leafColor: "#ff0000", leafDescription: "My desc", createdByUserId: Arg.Any<Guid?>(), ct: Arg.Any<CancellationToken>());
        Assert.Equal("My desc", result.Description);
        Assert.Equal("#ff0000", result.Color);
    }

    [Fact]
    public async Task Handle_MapsResolvedTagToResponse()
    {
        var parentId = Guid.NewGuid();
        var tag = new Tag
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            Path = "Parent/Test",
            ParentId = parentId,
            Color = "#ff0000",
            EffectiveColor = "#ff0000",
        };

        _tagPathResolver.ResolveOrCreateAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(tag);

        var result = await _sut.HandleAsync(new CreateInventoryTagCommand("Parent/Test", Color: "#ff0000"), CancellationToken.None);

        Assert.Equal(tag.Id, result.Id);
        Assert.Equal("Test", result.Name);
        Assert.Equal("Parent/Test", result.Path);
        Assert.Equal(parentId, result.ParentId);
        Assert.Equal("#ff0000", result.EffectiveColor);
    }

    [Fact]
    public async Task Handle_WithNullDescription_ReturnsNullDescription()
    {
        var tag = new Tag { Id = Guid.NewGuid(), Name = "Test", Path = "Test" };

        _tagPathResolver.ResolveOrCreateAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(tag);

        var result = await _sut.HandleAsync(new CreateInventoryTagCommand("Test"), CancellationToken.None);

        Assert.Null(result.Description);
    }

    [Fact]
    public async Task Handle_PersistsViaUnitOfWork()
    {
        var tag = new Tag { Id = Guid.NewGuid(), Name = "Test", Path = "Test" };
        _tagPathResolver.ResolveOrCreateAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(tag);

        await _sut.HandleAsync(new CreateInventoryTagCommand("Test"), CancellationToken.None);

        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
