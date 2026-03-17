using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Settings;
using Homestead.WebApi.Features.Admin.v1.OcrEngines.DeleteOcrEngine;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Admin;

public class DeleteOcrEngineHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditLogger _auditLogger;
    private readonly DeleteOcrEngineHandler _sut;

    public DeleteOcrEngineHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _currentUser = Substitute.For<ICurrentUserAccessor>();
        _currentUser.UserId.Returns(Guid.NewGuid());
        _auditLogger = Substitute.For<IAuditLogger>();
        _sut = new DeleteOcrEngineHandler(_db, _currentUser, _auditLogger);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task HandleAsync_BuiltInId_ThrowsBadRequest()
    {
        var request = new DeleteOcrEngineRequest(BuiltInOcrEngines.TesseractDefaultId);

        var ex = await Assert.ThrowsAsync<BadRequestException>(
            () => _sut.HandleAsync(request, CancellationToken.None));

        Assert.Equal("Cannot delete a built-in engine.", ex.Message);
    }

    [Fact]
    public async Task HandleAsync_NotFound_ThrowsNotFound()
    {
        var request = new DeleteOcrEngineRequest(Guid.NewGuid());

        await Assert.ThrowsAsync<NotFoundException>(
            () => _sut.HandleAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_ValidRequest_DeletesEngine()
    {
        var engineId = Guid.NewGuid();
        var engine = new OcrEngine
        {
            Id = engineId,
            Name = "To Be Deleted",
            EngineType = OcrEngineType.Tesseract,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.OcrEngines.Add(engine);
        await _db.SaveChangesAsync();

        var request = new DeleteOcrEngineRequest(engineId);

        var result = await _sut.HandleAsync(request, CancellationToken.None);

        Assert.True(result);

        var deleted = await _db.OcrEngines.FirstOrDefaultAsync(e => e.Id == engineId);
        Assert.Null(deleted);
    }
}
